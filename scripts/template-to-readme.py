#!/usr/bin/env python3
"""Put the Template spreadsheet into README.md as a Markdown table, card images included.

Reads a CSV exported from Template.ods by LibreOffice (with formulas recalculated
and cells saved as shown, so prices keep their £ and totals are current), and puts
the table between these two markers in README.md, replacing whatever is there:

    <!-- template-table:start -->
    <!-- template-table:end -->

The pictures placed in cells of Template.ods are copied into images/template/ and
shown in their cells. They're linked by full raw.githubusercontent.com URL so they
also show on the website, which copies only the README's text.

Usage: template-to-readme.py Template.csv Template.ods [README.md]
The update-readme-template workflow does the LibreOffice export, see it for the command.
"""
import csv
import html
import os
import re
import sys
import zipfile
import xml.etree.ElementTree as ET

START = "<!-- template-table:start -->"
END = "<!-- template-table:end -->"
IMAGE_DIR = "images/template"
IMAGE_HEIGHT = 64  # px, about the 1.7cm the pictures are in the sheet
REPO = os.environ.get("GITHUB_REPOSITORY", "ProfessorShroom/Cardmarket-Price-Updater")
IMAGE_URL = f"https://raw.githubusercontent.com/{REPO}/main/{IMAGE_DIR}"

NS = {
    "table": "urn:oasis:names:tc:opendocument:xmlns:table:1.0",
    "draw": "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0",
    "xlink": "http://www.w3.org/1999/xlink",
}
T = "{%s}" % NS["table"]
HREF = "{%s}href" % NS["xlink"]


def read_rows(path):
    with open(path, encoding="utf-8", newline="") as f:
        return [[c.strip() for c in r] for r in csv.reader(f)]


def extract_images(ods):
    """Copy the pictures in the first sheet's cells to IMAGE_DIR. Returns {(row, col): filename}."""
    found = {}
    with zipfile.ZipFile(ods) as z:
        sheet = ET.fromstring(z.read("content.xml")).find(".//table:table", NS)
        r = 0
        for row in sheet.iter(T + "table-row"):
            c = 0
            for cell in row:
                if cell.tag not in (T + "table-cell", T + "covered-table-cell"):
                    continue
                img = cell.find("draw:frame/draw:image", NS)
                if img is not None and img.get(HREF, "").startswith("Pictures/"):
                    found[(r, c)] = img.get(HREF)
                c += int(cell.get(T + "number-columns-repeated", "1"))
            r += int(row.get(T + "number-rows-repeated", "1"))

        os.makedirs(IMAGE_DIR, exist_ok=True)
        names = {}
        for pos, member in found.items():
            name = os.path.basename(member)
            with open(os.path.join(IMAGE_DIR, name), "wb") as f:
                f.write(z.read(member))
            names[pos] = name

    # Remove pictures that are no longer in the sheet
    for old in os.listdir(IMAGE_DIR):
        if old not in names.values():
            os.remove(os.path.join(IMAGE_DIR, old))
    return names


def to_markdown(rows, images):
    def cell(s):
        return s.replace("|", "\\|").replace("\r", "").replace("\n", "<br>")

    # Make sure rows/columns holding a picture aren't trimmed as empty
    for r, c in images:
        while len(rows) <= r:
            rows.append([])
        rows[r] += [""] * (c + 1 - len(rows[r]))
        rows[r][c] = rows[r][c] or "\0"  # placeholder, replaced below

    # Trim the empty rows and columns around the data
    while rows and not any(rows[-1]):
        rows.pop()
    width = max((max((i + 1 for i, c in enumerate(r) if c), default=0) for r in rows), default=0)
    rows = [(r + [""] * width)[:width] for r in rows]

    header = rows[0]
    alt_col = header.index("Set Code") if "Set Code" in header else None
    out = []
    for r, row in enumerate(rows):
        cells = [cell(v) for v in row]
        for c in range(width):
            if (r, c) in images:
                alt = row[alt_col] if alt_col is not None and row[alt_col] != "\0" else "Card"
                cells[c] = (f'<img src="{IMAGE_URL}/{images[(r, c)]}" '
                            f'alt="{html.escape(alt)}" height="{IMAGE_HEIGHT}">')
        out.append(cells)

    lines = ["| " + " | ".join(out[0]) + " |", "| " + " | ".join("---" for _ in out[0]) + " |"]
    lines += ["| " + " | ".join(r) + " |" for r in out[1:]]
    return "\n".join(lines)


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    src, ods = sys.argv[1], sys.argv[2]
    readme = sys.argv[3] if len(sys.argv) > 3 else "README.md"
    table = to_markdown(read_rows(src), extract_images(ods))

    with open(readme, encoding="utf-8") as f:
        text = f.read()
    pattern = re.compile(re.escape(START) + r".*?" + re.escape(END), re.S)
    if not pattern.search(text):
        sys.exit(f"{readme} has no {START} ... {END} markers")
    new = pattern.sub(lambda _: f"{START}\n\n{table}\n\n{END}", text, count=1)

    if new != text:
        with open(readme, "w", encoding="utf-8") as f:
            f.write(new)
        print(f"Updated the table in {readme}")
    else:
        print(f"{readme} table already up to date")


if __name__ == "__main__":
    main()
