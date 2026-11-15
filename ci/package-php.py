#!/usr/bin/env python3
"""Create an installable Composer artifact with composer.json at the archive root."""
import argparse
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def package(output):
    source = ROOT / "packages/php"
    metadata = json.loads((source / "composer.json").read_text())
    version = (ROOT / "VERSION").read_text().strip()
    if metadata["version"] != version:
        raise ValueError("PHP package version must match VERSION")
    output.mkdir(parents=True, exist_ok=True)
    target = output / f"artifactd-client-feedback-{version}.zip"
    files = [source / name for name in ("composer.json", "README.md", "LICENSE", "autoload.php")]
    files += sorted((source / "src").glob("*.php"))
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        for file in files:
            archive.write(file, file.relative_to(source).as_posix())
    print(target)
    return target


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "packages/php/dist")
    args = parser.parse_args()
    package(args.output)
