#!/usr/bin/env python3
"""Keep stable package versions aligned with VERSION (stdlib, Python 3.10+)."""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
action = parser.add_mutually_exclusive_group(required=True)
action.add_argument("--check", action="store_true")
action.add_argument("--set", metavar="X.Y.Z")
args = parser.parse_args()
version = args.set or (ROOT / "VERSION").read_text().strip()
if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version):
    parser.error("version must be stable X.Y.Z")
paths = [ROOT / "packages/python/pyproject.toml", ROOT / "packages/rust/Cargo.toml"]
node = ROOT / "packages/node/package.json"
metadata = json.loads(node.read_text())
php = ROOT / "packages/php/composer.json"
php_example = ROOT / "examples/php/composer.json"
if args.set:
    (ROOT / "VERSION").write_text(version + "\n")
    metadata["version"] = version
    node.write_text(json.dumps(metadata, indent=2) + "\n")
    php_metadata = json.loads(php.read_text())
    php_metadata["version"] = version
    php.write_text(json.dumps(php_metadata, indent=2) + "\n")
    example_metadata = json.loads(php_example.read_text())
    example_metadata["require"]["artifactd/client-feedback"] = version
    php_example.write_text(json.dumps(example_metadata, indent=2) + "\n")
    for path in paths:
        path.write_text(re.sub(r'^version = "[^"]+"', f'version = "{version}"', path.read_text(), count=1, flags=re.M))
    lock = ROOT / "packages/rust/Cargo.lock"
    if lock.exists():
        lock.write_text(re.sub(r'(name = "artifactd-client-feedback"\nversion = ")[^"]+', lambda m: m[1] + version, lock.read_text(), count=1))
else:
    versions = {str(node.relative_to(ROOT)): metadata["version"]}
    versions[str(php.relative_to(ROOT))] = json.loads(php.read_text())["version"]
    versions[str(php_example.relative_to(ROOT))] = json.loads(php_example.read_text())["require"]["artifactd/client-feedback"]
    for path in paths:
        versions[str(path.relative_to(ROOT))] = re.search(r'^version = "([^"]+)"', path.read_text(), re.M)[1]
    for name, actual in versions.items():
        if actual != version:
            parser.exit(1, f"{name}: {actual} differs from VERSION {version}\n")
    lock = ROOT / "packages/rust/Cargo.lock"
    if lock.exists():
        locked = re.search(r'name = "artifactd-client-feedback"\nversion = "([^"]+)"', lock.read_text())[1]
        if locked != version:
            parser.exit(1, "Rust lockfile version differs from VERSION\n")
print(f"All stable SDK versions: {version}")
