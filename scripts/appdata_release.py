import re
import sys
from datetime import date
from xml.sax.saxutils import escape

from readme_changelog import parse

APPDATA = "packaging/flatpak/io.github.professorshroom.CardmarketPriceUpdater.appdata.xml"


def load(path):
    with open(path, encoding="utf-8") as file:
        return parse(file.read())


def render(version, notes, when):
    lines = [f'    <release version="{version}" date="{when}">']
    if notes:
        lines += ["      <description>", "        <ul>"]
        for note in notes:
            text = " ".join(note.replace("`", "").replace("**", "").split())
            lines.append(f"          <li>{escape(text)}</li>")
        lines += ["        </ul>", "      </description>"]
    lines.append("    </release>")
    return "\n".join(lines) + "\n"


def main():
    full = load("CHANGELOG.md")
    if len(sys.argv) > 1:
        version = sys.argv[1].removeprefix("v")
        if not re.fullmatch(r"\d+(\.\d+)*(b\d+)?", version):
            sys.exit(f"'{version}' isn't a valid version")
        entry = next((v for v in full + load("CHANGELOG-BETA.md") if v["name"] == version), None)
        if entry is None:
            print(f"No changelog section for {version}, adding it without notes")
        notes = entry["notes"] if entry else []
    else:
        entry = next((v for v in full if not re.search(r"\db\d+$", v["name"])), None)
        if entry is None:
            sys.exit("CHANGELOG.md doesn't have any versions in it")
        version, notes = entry["name"], entry["notes"]

    with open(APPDATA, encoding="utf-8") as file:
        appdata = file.read()

    pattern = re.compile(rf'    <release version="{re.escape(version)}" date="([^"]*)">.*?</release>\n', re.S)
    existing = pattern.search(appdata)
    when = sys.argv[2] if len(sys.argv) > 2 else existing[1] if existing else date.today().isoformat()
    appdata = pattern.sub("", appdata)

    if "<releases>\n" not in appdata:
        sys.exit(f"{APPDATA} doesn't have a <releases> section")
    appdata = appdata.replace("<releases>\n", "<releases>\n" + render(version, notes, when), 1)
    with open(APPDATA, "w", encoding="utf-8", newline="\n") as file:
        file.write(appdata)


if __name__ == "__main__":
    main()
