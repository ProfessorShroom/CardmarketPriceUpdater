#!/usr/bin/env python3
"""Copy Template.ods with its first sheet set to print on one landscape page.

The print range covers only cells with visible text or a picture, so empty
formula cells further down don't shrink the render. Used to make the
spreadsheet screenshot with LibreOffice's PNG export.

Usage: template-print-range.py Template.ods out.ods
"""

import re
import sys
import zipfile
import xml.etree.ElementTree as ET

TABLE = "urn:oasis:names:tc:opendocument:xmlns:table:1.0"


def column_name(index):
    name = ""
    index += 1
    while index:
        index, remainder = divmod(index - 1, 26)
        name = chr(65 + remainder) + name
    return name


def used_range(content):
    table = ET.fromstring(content).find(f".//{{{TABLE}}}table")
    last_row = last_col = 0
    row_index = 0
    for row in table.iter(f"{{{TABLE}}}table-row"):
        rows_repeated = int(row.get(f"{{{TABLE}}}number-rows-repeated", "1"))
        col_index = 0
        for cell in row:
            if not cell.tag.endswith(("table-cell", "covered-table-cell")):
                continue
            cols_repeated = int(cell.get(f"{{{TABLE}}}number-columns-repeated", "1"))
            visible = "".join(cell.itertext()).strip() or any(child.tag.endswith("frame") for child in cell)
            if visible:
                last_row = max(last_row, row_index)
                last_col = max(last_col, col_index)
            col_index += cols_repeated
        row_index += rows_repeated
    return table.get(f"{{{TABLE}}}name"), last_row, last_col


def fit_page_layout(match):
    props = match.group(0)
    props = re.sub(r'style:scale-to(-pages|-X|-Y)?="[^"]*"', "", props)
    props = props.replace("<style:page-layout-properties", '<style:page-layout-properties style:scale-to-X="1" style:scale-to-Y="1"', 1)
    props = re.sub(r'style:print-orientation="[^"]*"', 'style:print-orientation="landscape"', props)
    props = re.sub(r'fo:page-width="[^"]*"', 'fo:page-width="29.7cm"', props)
    props = re.sub(r'fo:page-height="[^"]*"', 'fo:page-height="21.001cm"', props)
    props = re.sub(r'fo:margin-(top|bottom|left|right)="[^"]*"', r'fo:margin-\1="0.5cm"', props)
    return props


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    source, destination = sys.argv[1], sys.argv[2]

    with zipfile.ZipFile(source) as zin, zipfile.ZipFile(destination, "w") as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename == "content.xml":
                name, last_row, last_col = used_range(data)
                print_range = f"'{name}'.A1:'{name}'.{column_name(last_col)}{last_row + 1}"
                text = re.sub(r'\s*table:print-ranges="[^"]*"', "", data.decode("utf-8"))
                text = text.replace(
                    f'<table:table table:name="{name}"',
                    f'<table:table table:name="{name}" table:print-ranges="{print_range}"',
                    1,
                )
                data = text.encode("utf-8")
                print(f"Print range: {print_range}")
            elif item.filename == "styles.xml":
                text = re.sub(
                    r"<style:page-layout-properties[^>]*fo:page-width[^>]*>",
                    fit_page_layout,
                    data.decode("utf-8"),
                )
                data = text.encode("utf-8")
            compression = zipfile.ZIP_STORED if item.filename == "mimetype" else zipfile.ZIP_DEFLATED
            zout.writestr(item, data, compress_type=compression)


if __name__ == "__main__":
    main()
