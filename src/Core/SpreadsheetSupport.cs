using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

// Copyright © Charlie Howard 2026 All rights reserved.

namespace Cardmarket_Price_Updater.Core
{
    /// <summary>
    /// The handful of operations PriceUpdater needs from a worksheet.
    /// Rows and columns are 1-based, like EPPlus.
    /// </summary>
    internal interface ISheet
    {
        /// <summary>Last row that contains anything (0 if the sheet is empty).</summary>
        int LastRow { get; }

        /// <summary>Right-most column that contains anything (0 if the sheet is empty).</summary>
        int LastCol { get; }

        /// <summary>The cell's displayed text, or "" if the cell is empty.</summary>
        string GetText(int row, int col);

        void SetNumber(int row, int col, decimal value);
        void SetText(int row, int col, string value);
    }

    /// <summary>.xlsx worksheet (EPPlus).</summary>
    internal sealed class EpplusSheet : ISheet
    {
        private readonly ExcelWorksheet _ws;

        public EpplusSheet(ExcelWorksheet ws) => _ws = ws;

        public int LastRow => _ws.Dimension?.End.Row ?? 0;
        public int LastCol => _ws.Dimension?.End.Column ?? 0;

        public string GetText(int row, int col) => _ws.Cells[row, col].Text;
        public void SetNumber(int row, int col, decimal value) => _ws.Cells[row, col].Value = value;
        public void SetText(int row, int col, string value) => _ws.Cells[row, col].Value = value;
    }

    /// <summary>
    /// An OpenDocument spreadsheet (.ods, as written by LibreOffice / OpenOffice).
    ///
    /// Only content.xml is touched, and only the cells we write to are modified, so styles,
    /// column widths, other sheets, etc. all survive. Every other entry in the zip is copied
    /// across untouched (mimetype stays first and uncompressed, as the spec requires).
    /// Only the first sheet is exposed, matching how the .xlsx path behaves.
    /// </summary>
    internal sealed class OdsWorkbook
    {
        private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        private static readonly XNamespace CalcExt = "urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0";

        private readonly byte[] _original;
        private readonly XDocument _content;

        public OdsSheet FirstSheet { get; }

        private OdsWorkbook(byte[] original, XDocument content)
        {
            _original = original;
            _content = content;

            var sheetEl = content.Root?
                .Element(Office + "body")?
                .Element(Office + "spreadsheet")?
                .Elements(Table + "table")
                .FirstOrDefault()
                ?? throw new InvalidDataException("This .ods file doesn't contain a spreadsheet with any sheets.");

            FirstSheet = new OdsSheet(sheetEl);
        }

        public static OdsWorkbook Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

            var entry = zip.GetEntry("content.xml")
                ?? throw new InvalidDataException("Not a valid .ods file (content.xml is missing).");

            try
            {
                using var s = entry.Open();
                // PreserveWhitespace so we never alter text inside <text:p> elements
                var doc = XDocument.Load(s, LoadOptions.PreserveWhitespace);
                return new OdsWorkbook(bytes, doc);
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException(
                    "Could not read content.xml from the .ods file - is it password protected or corrupt? " + ex.Message, ex);
            }
        }

        public void Save(string path)
        {
            using var src = new ZipArchive(new MemoryStream(_original), ZipArchiveMode.Read);
            using var outStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var dst = new ZipArchive(outStream, ZipArchiveMode.Create);

            foreach (var e in src.Entries)
            {
                if (e.FullName.EndsWith("/"))
                {
                    dst.CreateEntry(e.FullName); // empty directory entry
                    continue;
                }

                // The spec says "mimetype" must be the first entry and must not be compressed
                var level = e.FullName == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                var ne = dst.CreateEntry(e.FullName, level);
                ne.LastWriteTime = e.LastWriteTime;

                using var ns = ne.Open();
                if (e.FullName == "content.xml")
                {
                    var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false };
                    using var xw = XmlWriter.Create(ns, settings);
                    _content.Save(xw);
                }
                else
                {
                    using var es = e.Open();
                    es.CopyTo(ns);
                }
            }
        }

        // ------------------------------------------------------------------------------------

        internal sealed class OdsSheet : ISheet
        {
            private static readonly XName RowsRepeated = Table + "number-rows-repeated";
            private static readonly XName ColsRepeated = Table + "number-columns-repeated";
            private static readonly XName CellName = Table + "table-cell";
            private static readonly XName CoveredCellName = Table + "covered-table-cell";

            private readonly XElement _table;
            private readonly List<RowSpan> _rows = new();
            private bool _dirty = true;

            private readonly record struct RowSpan(XElement El, int Start, int Count);
            private readonly record struct CellSpan(XElement El, int Start, int Count);

            public OdsSheet(XElement table) => _table = table;

            // ---------- extent ----------

            public int LastRow
            {
                get
                {
                    EnsureRows();
                    int last = 0;
                    foreach (var s in _rows)
                        if (LastNonEmptyCol(s.El) > 0)
                            last = s.Start + s.Count - 1;
                    return last;
                }
            }

            public int LastCol
            {
                get
                {
                    EnsureRows();
                    int last = 0;
                    foreach (var s in _rows)
                        last = Math.Max(last, LastNonEmptyCol(s.El));
                    return last;
                }
            }

            // ---------- reading ----------

            public string GetText(int row, int col)
            {
                int idx = FindRowIndex(row);
                if (idx < 0) return string.Empty;

                foreach (var span in CellSpans(_rows[idx].El))
                    if (col >= span.Start && col < span.Start + span.Count)
                        return CellText(span.El);

                return string.Empty;
            }

            // ---------- writing ----------

            public void SetNumber(int row, int col, decimal value)
            {
                var cell = GetCellForWrite(row, col);

                // Keep currency cells as currency (so the £/$/€ formatting survives); everything else becomes a plain float
                string type = (string?)cell.Attribute(Office + "value-type") == "currency" ? "currency" : "float";

                ClearValue(cell);
                cell.SetAttributeValue(Office + "value-type", type);
                cell.SetAttributeValue(Office + "value", value.ToString(CultureInfo.InvariantCulture));
                if (cell.Attribute(CalcExt + "value-type") != null)
                    cell.SetAttributeValue(CalcExt + "value-type", type);

                // Display text (appended, so it stays after any cell comment, as the schema requires). LibreOffice regenerates
                // this from the cell style on load; other readers use it as-is
                cell.Add(new XElement(Text + "p", value.ToString("0.00", CultureInfo.InvariantCulture)));
            }

            public void SetText(int row, int col, string value)
            {
                var cell = GetCellForWrite(row, col);

                ClearValue(cell);
                cell.SetAttributeValue(Office + "currency", null);
                cell.SetAttributeValue(Office + "value-type", "string");
                if (cell.Attribute(CalcExt + "value-type") != null)
                    cell.SetAttributeValue(CalcExt + "value-type", "string");

                cell.Add(new XElement(Text + "p", value));
            }

            private static void ClearValue(XElement cell)
            {
                cell.SetAttributeValue(Office + "value", null);
                cell.SetAttributeValue(Office + "string-value", null);
                cell.SetAttributeValue(Office + "date-value", null);
                cell.SetAttributeValue(Office + "time-value", null);
                cell.SetAttributeValue(Office + "boolean-value", null);
                cell.SetAttributeValue(Table + "formula", null); // we're overwriting the value, so drop any formula
                cell.Elements(Text + "p").Remove();
            }

            // ---------- row / cell lookup ----------

            private void EnsureRows()
            {
                if (!_dirty) return;
                _rows.Clear();

                int pos = 1;
                foreach (var el in EnumerateRows(_table))
                {
                    int n = RepeatCount(el, RowsRepeated);
                    _rows.Add(new RowSpan(el, pos, n));
                    pos += n;
                }
                _dirty = false;
            }

            private static IEnumerable<XElement> EnumerateRows(XElement parent)
            {
                foreach (var child in parent.Elements())
                {
                    if (child.Name == Table + "table-row")
                    {
                        yield return child;
                    }
                    else if (child.Name == Table + "table-header-rows"
                          || child.Name == Table + "table-rows"
                          || child.Name == Table + "table-row-group")
                    {
                        foreach (var r in EnumerateRows(child))
                            yield return r;
                    }
                }
            }

            private int FindRowIndex(int row)
            {
                EnsureRows();
                int lo = 0, hi = _rows.Count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    var s = _rows[mid];
                    if (row < s.Start) hi = mid - 1;
                    else if (row >= s.Start + s.Count) lo = mid + 1;
                    else return mid;
                }
                return -1;
            }

            private static List<CellSpan> CellSpans(XElement row)
            {
                var list = new List<CellSpan>();
                int pos = 1;
                foreach (var el in row.Elements())
                {
                    if (el.Name != CellName && el.Name != CoveredCellName) continue;
                    int n = RepeatCount(el, ColsRepeated);
                    list.Add(new CellSpan(el, pos, n));
                    pos += n;
                }
                return list;
            }

            private static int LastNonEmptyCol(XElement row)
            {
                int last = 0;
                foreach (var span in CellSpans(row))
                    if (IsNonEmpty(span.El))
                        last = span.Start + span.Count - 1;
                return last;
            }

            private static bool IsNonEmpty(XElement cell) =>
                cell.Attribute(Office + "value-type") != null || cell.Elements(Text + "p").Any();

            private static int RepeatCount(XElement el, XName attr)
            {
                var a = el.Attribute(attr);
                return a != null && int.TryParse(a.Value, out int n) && n > 0 ? n : 1;
            }

            private static void SetRepeat(XElement el, XName attr, int n) =>
                el.SetAttributeValue(attr, n > 1 ? n.ToString(CultureInfo.InvariantCulture) : null);

            /// <summary>
            /// ODS stores runs of identical rows/cells as one element with a "repeated" count.
            /// To edit one row we first split it out of its run.
            /// </summary>
            private XElement GetRowForWrite(int row)
            {
                int idx = FindRowIndex(row);
                if (idx < 0)
                    throw new InvalidOperationException($"Row {row} doesn't exist in the .ods sheet.");

                var span = _rows[idx];
                if (span.Count == 1) return span.El;

                int before = row - span.Start;
                int after = span.Count - before - 1;

                if (before > 0)
                {
                    var b = new XElement(span.El);
                    SetRepeat(b, RowsRepeated, before);
                    span.El.AddBeforeSelf(b);
                }
                if (after > 0)
                {
                    var a = new XElement(span.El);
                    SetRepeat(a, RowsRepeated, after);
                    span.El.AddAfterSelf(a);
                }
                SetRepeat(span.El, RowsRepeated, 1);

                _dirty = true;
                return span.El;
            }

            private XElement GetCellForWrite(int row, int col)
            {
                var rowEl = GetRowForWrite(row);
                var spans = CellSpans(rowEl);
                int total = spans.Count == 0 ? 0 : spans[^1].Start + spans[^1].Count - 1;

                // Short row: pad out to the column we need
                if (col > total)
                {
                    int gap = col - total - 1;
                    if (gap > 0)
                    {
                        var pad = new XElement(CellName);
                        SetRepeat(pad, ColsRepeated, gap);
                        rowEl.Add(pad);
                    }
                    var nc = new XElement(CellName);
                    rowEl.Add(nc);
                    return nc;
                }

                foreach (var span in spans)
                {
                    if (col < span.Start || col >= span.Start + span.Count) continue;
                    if (span.Count == 1) return span.El;

                    int before = col - span.Start;
                    int after = span.Count - before - 1;

                    if (before > 0)
                    {
                        var b = new XElement(span.El);
                        SetRepeat(b, ColsRepeated, before);
                        span.El.AddBeforeSelf(b);
                    }
                    if (after > 0)
                    {
                        var a = new XElement(span.El);
                        SetRepeat(a, ColsRepeated, after);
                        span.El.AddAfterSelf(a);
                    }
                    SetRepeat(span.El, ColsRepeated, 1);
                    return span.El;
                }

                throw new InvalidOperationException($"Could not locate cell ({row}, {col}).");
            }

            // ---------- text extraction ----------

            private static string CellText(XElement cell)
            {
                var paragraphs = cell.Elements(Text + "p").ToList();
                if (paragraphs.Count > 0)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < paragraphs.Count; i++)
                    {
                        if (i > 0) sb.Append('\n');
                        AppendText(paragraphs[i], sb);
                    }
                    if (sb.Length > 0) return sb.ToString();
                }

                // Some generators omit <text:p> and only write the raw value
                var sv = (string?)cell.Attribute(Office + "string-value");
                if (sv != null) return sv;

                var v = (string?)cell.Attribute(Office + "value");
                if (v != null)
                {
                    if (decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == Math.Truncate(d))
                        return d.ToString("0", CultureInfo.InvariantCulture); // "12345.0" -> "12345" so ID parsing works
                    return v;
                }

                return (string?)cell.Attribute(Office + "date-value") ?? string.Empty;
            }

            private static void AppendText(XElement el, StringBuilder sb)
            {
                foreach (var node in el.Nodes())
                {
                    if (node is XText t)
                    {
                        sb.Append(t.Value);
                    }
                    else if (node is XElement child)
                    {
                        if (child.Name == Text + "s")
                        {
                            int count = RepeatCount(child, Text + "c");
                            sb.Append(' ', count);
                        }
                        else if (child.Name == Text + "tab") sb.Append('\t');
                        else if (child.Name == Text + "line-break") sb.Append('\n');
                        else AppendText(child, sb); // text:span, text:a, etc.
                    }
                }
            }
        }
    }
}
