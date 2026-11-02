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
using CompressionLevel = System.IO.Compression.CompressionLevel;

// Copyright © Charlie Howard 2025-2026 All rights reserved

namespace Cardmarket_Price_Updater.Core
{
    internal interface ISheet
    {
        int LastRow { get; }
        int LastCol { get; }
        string GetText(int row, int col);
        void SetNumber(int row, int col, decimal value);
        void SetText(int row, int col, string value);
        bool HasImage(int row, int col);
        void SetImage(int row, int col, byte[] data);
    }

    internal sealed class EpplusSheet : ISheet
    {
        private readonly ExcelWorksheet _ws;

        public EpplusSheet(ExcelWorksheet ws) => _ws = ws;

        public int LastRow => _ws.Dimension?.End.Row ?? 0;
        public int LastCol => _ws.Dimension?.End.Column ?? 0;

        public string GetText(int row, int col) => _ws.Cells[row, col].Text;
        public void SetNumber(int row, int col, decimal value) => _ws.Cells[row, col].Value = value;
        public void SetText(int row, int col, string value) => _ws.Cells[row, col].Value = value;

        public bool HasImage(int row, int col) =>
            _ws.Drawings.Any(d => d.From.Row == row - 1 && d.From.Column == col - 1);

        public void SetImage(int row, int col, byte[] data)
        {
            const double pxPerCm = 96.0 / ImageLayout.CmPerInch;
            const double pointsPerCm = 72.0 / ImageLayout.CmPerInch;

            var (wCm, hCm) = ImageInfo.FitCm(data);

            _ws.Row(row).Height = ImageLayout.RowHeightCm * pointsPerCm;
            _ws.Column(col).Width = Math.Max(_ws.Column(col).Width, (ImageLayout.ColumnWidthCm * pxPerCm - 5) / 7.0);

            using var ms = new MemoryStream(data);
            var pic = _ws.Drawings.AddPicture("cardImage" + Guid.NewGuid().ToString("N")[..8], ms);
            pic.SetPosition(row - 1, 2, col - 1, 3);
            pic.SetSize((int)Math.Round(wCm * pxPerCm), (int)Math.Round(hCm * pxPerCm));
        }
    }

    internal sealed class OdsWorkbook
    {
        private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        private static readonly XNamespace CalcExt = "urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0";
        private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
        private static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
        private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
        private static readonly XNamespace StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
        private static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
        private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

        private const string ImgRowStyle = "cmuImgRow";
        private const string ImgColStyle = "cmuImgCol";
        private const string ImgGraphicStyle = "cmuImgGr";

        private readonly byte[] _original;
        private readonly XDocument _content;

        private readonly Dictionary<string, (byte[] Data, string Mime)> _newPictures = new();
        private bool _imageStylesReady;

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

            FirstSheet = new OdsSheet(sheetEl, this);
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
                var doc = XDocument.Load(s, LoadOptions.PreserveWhitespace);
                return new OdsWorkbook(bytes, doc);
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException(
                    "Could not read content.xml from the .ods file - is it password protected or corrupt? " + ex.Message, ex);
            }
        }

        private void EnsureImageStyles()
        {
            if (_imageStylesReady) return;

            var root = _content.Root!;
            var auto = root.Element(Office + "automatic-styles");
            if (auto == null)
            {
                auto = new XElement(Office + "automatic-styles");
                var body = root.Element(Office + "body");
                if (body != null) body.AddBeforeSelf(auto); else root.Add(auto);
            }

            string cm(double v) => v.ToString("0.###", CultureInfo.InvariantCulture) + "cm";

            auto.Add(new XElement(StyleNs + "style",
                new XAttribute(StyleNs + "name", ImgRowStyle),
                new XAttribute(StyleNs + "family", "table-row"),
                new XElement(StyleNs + "table-row-properties",
                    new XAttribute(StyleNs + "row-height", cm(ImageLayout.RowHeightCm)),
                    new XAttribute(StyleNs + "use-optimal-row-height", "false"))));

            auto.Add(new XElement(StyleNs + "style",
                new XAttribute(StyleNs + "name", ImgColStyle),
                new XAttribute(StyleNs + "family", "table-column"),
                new XElement(StyleNs + "table-column-properties",
                    new XAttribute(StyleNs + "column-width", cm(ImageLayout.ColumnWidthCm)))));

            auto.Add(new XElement(StyleNs + "style",
                new XAttribute(StyleNs + "name", ImgGraphicStyle),
                new XAttribute(StyleNs + "family", "graphic"),
                new XElement(StyleNs + "graphic-properties",
                    new XAttribute(StyleNs + "vertical-pos", "top"),
                    new XAttribute(StyleNs + "vertical-rel", "paragraph"),
                    new XAttribute(StyleNs + "horizontal-pos", "left"),
                    new XAttribute(StyleNs + "horizontal-rel", "paragraph"),
                    new XAttribute(StyleNs + "wrap", "none"),
                    new XAttribute(StyleNs + "mirror", "none"),
                    new XAttribute(Fo + "clip", "rect(0cm, 0cm, 0cm, 0cm)"))));

            _imageStylesReady = true;
        }

        private string AddPicture(byte[] data)
        {
            string path = $"Pictures/cmu{ImageInfo.Hash(data)}.{ImageInfo.Extension(data)}";
            _newPictures[path] = (data, ImageInfo.MimeType(data));
            return path;
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
                    dst.CreateEntry(e.FullName);
                    continue;
                }

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
                else if (e.FullName == "META-INF/manifest.xml" && _newPictures.Count > 0)
                {
                    XDocument manifest;
                    using (var ms = e.Open())
                        manifest = XDocument.Load(ms, LoadOptions.PreserveWhitespace);

                    var existing = new HashSet<string>(
                        manifest.Root!.Elements(Manifest + "file-entry")
                            .Select(x => (string?)x.Attribute(Manifest + "full-path") ?? string.Empty));

                    foreach (var (picPath, pic) in _newPictures)
                    {
                        if (!existing.Add(picPath)) continue;
                        manifest.Root.Add(new XElement(Manifest + "file-entry",
                            new XAttribute(Manifest + "full-path", picPath),
                            new XAttribute(Manifest + "media-type", pic.Mime)));
                    }

                    var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false };
                    using var xw = XmlWriter.Create(ns, settings);
                    manifest.Save(xw);
                }
                else
                {
                    using var es = e.Open();
                    es.CopyTo(ns);
                }
            }

            foreach (var (picPath, pic) in _newPictures)
            {
                if (src.GetEntry(picPath) != null) continue;

                var pe = dst.CreateEntry(picPath, CompressionLevel.NoCompression);
                using var ps = pe.Open();
                ps.Write(pic.Data, 0, pic.Data.Length);
            }
        }

        internal sealed class OdsSheet : ISheet
        {
            private static readonly XName RowsRepeated = Table + "number-rows-repeated";
            private static readonly XName ColsRepeated = Table + "number-columns-repeated";
            private static readonly XName CellName = Table + "table-cell";
            private static readonly XName CoveredCellName = Table + "covered-table-cell";

            private readonly XElement _table;
            private readonly OdsWorkbook _wb;
            private readonly List<RowSpan> _rows = new();
            private bool _dirty = true;
            private readonly HashSet<int> _styledImageCols = new();

            private readonly record struct RowSpan(XElement El, int Start, int Count);
            private readonly record struct CellSpan(XElement El, int Start, int Count);

            public OdsSheet(XElement table, OdsWorkbook workbook)
            {
                _table = table;
                _wb = workbook;
            }

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

            // Read

            public string GetText(int row, int col)
            {
                int idx = FindRowIndex(row);
                if (idx < 0) return string.Empty;

                foreach (var span in CellSpans(_rows[idx].El))
                    if (col >= span.Start && col < span.Start + span.Count)
                        return CellText(span.El);

                return string.Empty;
            }

            // Write

            public void SetNumber(int row, int col, decimal value)
            {
                var cell = GetCellForWrite(row, col);
                string type = (string?)cell.Attribute(Office + "value-type") == "currency" ? "currency" : "float";

                ClearValue(cell);
                cell.SetAttributeValue(Office + "value-type", type);
                cell.SetAttributeValue(Office + "value", value.ToString(CultureInfo.InvariantCulture));
                if (cell.Attribute(CalcExt + "value-type") != null)
                    cell.SetAttributeValue(CalcExt + "value-type", type);

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

            public bool HasImage(int row, int col)
            {
                int idx = FindRowIndex(row);
                if (idx < 0) return false;

                foreach (var span in CellSpans(_rows[idx].El))
                    if (col >= span.Start && col < span.Start + span.Count)
                        return span.El.Elements(Draw + "frame").Any();

                return false;
            }

            public void SetImage(int row, int col, byte[] data)
            {
                _wb.EnsureImageStyles();

                if (_styledImageCols.Add(col))
                    SetColumnStyle(col, ImgColStyle);

                string href = _wb.AddPicture(data);
                var (wCm, hCm) = ImageInfo.FitCm(data);

                GetRowForWrite(row).SetAttributeValue(Table + "style-name", ImgRowStyle);

                var cell = GetCellForWrite(row, col);
                cell.Elements(Draw + "frame").Remove();

                string cm(double v) => v.ToString("0.###", CultureInfo.InvariantCulture) + "cm";

                var frame = new XElement(Draw + "frame",
                    new XAttribute(Draw + "style-name", ImgGraphicStyle),
                    new XAttribute(Draw + "layer", "layout"),
                    new XAttribute(Svg + "width", cm(wCm)),
                    new XAttribute(Svg + "height", cm(hCm)),
                    new XAttribute(Svg + "x", cm((ImageLayout.ColumnWidthCm - wCm) / 2)),
                    new XAttribute(Svg + "y", cm((ImageLayout.RowHeightCm - hCm) / 2)),
                    new XElement(Draw + "image",
                        new XAttribute(XLink + "href", href),
                        new XAttribute(XLink + "type", "simple"),
                        new XAttribute(XLink + "show", "embed"),
                        new XAttribute(XLink + "actuate", "onLoad")));

                cell.AddFirst(frame);
            }

            private void SetColumnStyle(int col, string styleName)
            {
                int pos = 1;
                foreach (var el in _table.Descendants(Table + "table-column").ToList())
                {
                    int n = RepeatCount(el, ColsRepeated);
                    if (col >= pos && col < pos + n)
                    {
                        int before = col - pos;
                        int after = n - before - 1;

                        if (before > 0)
                        {
                            var b = new XElement(el);
                            SetRepeat(b, ColsRepeated, before);
                            el.AddBeforeSelf(b);
                        }
                        if (after > 0)
                        {
                            var a = new XElement(el);
                            SetRepeat(a, ColsRepeated, after);
                            el.AddAfterSelf(a);
                        }

                        SetRepeat(el, ColsRepeated, 1);
                        el.SetAttributeValue(Table + "style-name", styleName);
                        return;
                    }
                    pos += n;
                }
            }

            private static void ClearValue(XElement cell)
            {
                cell.SetAttributeValue(Office + "value", null);
                cell.SetAttributeValue(Office + "string-value", null);
                cell.SetAttributeValue(Office + "date-value", null);
                cell.SetAttributeValue(Office + "time-value", null);
                cell.SetAttributeValue(Office + "boolean-value", null);
                cell.SetAttributeValue(Table + "formula", null);
                cell.Elements(Text + "p").Remove();
            }

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

                var sv = (string?)cell.Attribute(Office + "string-value");
                if (sv != null) return sv;

                var v = (string?)cell.Attribute(Office + "value");
                if (v != null)
                {
                    if (decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == Math.Truncate(d))
                        return d.ToString("0", CultureInfo.InvariantCulture);
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
                        else AppendText(child, sb);
                    }
                }
            }
        }
    }
}
