import re
import sys

CSPROJ = "src/Avalonia/Cardmarket-Price-Updater.App.csproj"


def numbers(version):
    return tuple(int(part) for part in version.split("."))


def main():
    version = sys.argv[1].removeprefix("v")
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version):
        sys.exit(f"'{version}' isn't a full release version")

    with open("update.xml", encoding="utf-8", newline="") as file:
        xml = file.read()
    current = re.search(r"<version>([\d.]+)</version>", xml)
    if not current:
        sys.exit("update.xml doesn't have a version")
    if numbers(version) <= numbers(current[1]):
        print(f"update.xml is already at {current[1]}, leaving it alone")
        return

    with open(CSPROJ, encoding="utf-8-sig") as file:
        built = re.search(r"<AssemblyVersion>([^<]*)</AssemblyVersion>", file.read())
    if not built or built[1] != version:
        sys.exit(f"The app was built as {built[1] if built else 'unknown'} but the tag is {version}, not updating update.xml")

    with open("update.xml", "w", encoding="utf-8", newline="") as file:
        file.write(xml.replace(current[0], f"<version>{version}</version>"))
    print(f"update.xml set to {version}")


if __name__ == "__main__":
    main()
