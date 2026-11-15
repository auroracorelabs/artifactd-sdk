#!/usr/bin/env python3
"""Validate GitHub Actions package artifacts and assemble a downloadable SDK bundle."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import tarfile
import zipfile

FORMATS = (
    ("php", "php-packages", "artifactd-client-feedback-*.zip", "src/ClientFeedbackClient.php"),
    ("dotnet", "dotnet-packages", "ArtifactD.ClientFeedback.*.nupkg", "lib/netstandard2.0/ArtifactD.ClientFeedback.dll"),
    ("dotnet", "dotnet-packages", "ArtifactD.ClientFeedback.*.snupkg", None),
    ("python", "python-packages", "artifactd_client_feedback-*.whl", "artifactd_client_feedback/__init__.py"),
    ("python", "python-packages", "artifactd_client_feedback-*.tar.gz", "src/artifactd_client_feedback/__init__.py"),
    ("node", "node-packages", "artifactd-client-feedback-*.tgz", "package/src/index.js"),
    ("rust", "rust-packages", "artifactd-client-feedback-*.crate", "src/lib.rs"),
)


def validate_archive(path, required):
    if path.suffix in (".nupkg", ".snupkg", ".whl", ".zip"):
        with zipfile.ZipFile(path) as archive:
            bad = archive.testzip()
            if bad:
                raise ValueError(f"Corrupt archive entry: {path.name}: {bad}")
            names = archive.namelist()
            if path.suffix == ".zip":
                if "composer.json" not in names or json.loads(archive.read("composer.json")).get("name") != "artifactd/client-feedback":
                    raise ValueError(f"{path.name} lacks the Composer package manifest at its root")
    else:
        with tarfile.open(path, "r:gz") as archive:
            names = archive.getnames()
    if not names:
        raise ValueError(f"Empty package: {path.name}")
    if required and not any(name == required or name.endswith('/' + required) for name in names):
        raise ValueError(f"{path.name} does not contain {required}")


def collect(input_dir, output_dir, version):
    files = []
    for language, artifact, pattern, required in FORMATS:
        candidates = sorted((input_dir / artifact).glob(pattern))
        if len(candidates) != 1:
            raise ValueError(f"Expected one {artifact}/{pattern}; found {len(candidates)}")
        path = candidates[0]
        if not path.is_file() or path.is_symlink():
            raise ValueError(f"Not a regular package file: {path}")
        validate_archive(path, required)
        files.append((language, path))
    if output_dir.exists() and any(output_dir.iterdir()):
        raise ValueError(f"Output directory must be empty: {output_dir}")
    output_dir.mkdir(parents=True, exist_ok=True)
    manifest = {"version": version, "commit": os.environ.get("GITHUB_SHA"), "packages": []}
    checksums = []
    for language, path in files:
        target = output_dir / language / path.name
        target.parent.mkdir(exist_ok=True)
        shutil.copyfile(path, target)
        digest = hashlib.sha256(target.read_bytes()).hexdigest()
        relative = target.relative_to(output_dir).as_posix()
        manifest["packages"].append({"language": language, "file": relative, "size_bytes": target.stat().st_size, "sha256": digest})
        checksums.append(f"{digest}  {relative}\n")
    (output_dir / "SHA256SUMS").write_text(''.join(checksums), encoding="utf-8")
    (output_dir / "manifest.json").write_text(json.dumps(manifest, indent=2) + '\n', encoding="utf-8")
    summary = "## Client Feedback packages\n\nDownload **client-feedback-packages** in this run's **Artifacts** section, or choose a language-specific artifact.\n\n| SDK | File | Bytes |\n| --- | --- | ---: |\n"
    for package in manifest["packages"]:
        summary += f"| {package['language']} | `{package['file']}` | {package['size_bytes']} |\n"
    summary += "\nThe bundle includes `SHA256SUMS` and `manifest.json`. Packages are installable without cloning this repository.\n"
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, 'a', encoding="utf-8") as stream:
            stream.write(summary)
    print(f"Validated and collected {len(files)} packages into {output_dir}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True, help="Stable release version from VERSION; individual packages may be prereleases")
    args = parser.parse_args()
    try:
        collect(args.input, args.output, args.version)
    except (ValueError, OSError, zipfile.BadZipFile, tarfile.TarError) as error:
        parser.exit(1, f"Artifact validation failed: {error}\n")
