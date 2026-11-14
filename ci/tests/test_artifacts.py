import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location("artifacts", Path(__file__).resolve().parents[1] / "collect-artifacts.py")
artifacts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(artifacts)


class ArtifactTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.inputs = self.base / 'inputs'
        self.output = self.base / 'output'
        for _, name, pattern, required in artifacts.FORMATS:
            path = self.inputs / name / pattern.replace('*', '1.2.3')
            path.parent.mkdir(parents=True, exist_ok=True)
            entry = required or 'symbols.pdb'
            if path.suffix in ('.nupkg', '.snupkg', '.whl', '.zip'):
                with zipfile.ZipFile(path, 'w') as archive:
                    archive.writestr(entry, b'test')
                    if path.suffix == '.zip':
                        archive.writestr('composer.json', json.dumps({'name': 'artifactd/client-feedback'}))
            else:
                with tarfile.open(path, 'w:gz') as archive:
                    info = tarfile.TarInfo(entry)
                    info.size = 4
                    archive.addfile(info, io.BytesIO(b'test'))

    def collect(self):
        with contextlib.redirect_stdout(io.StringIO()):
            artifacts.collect(self.inputs, self.output, '1.2.3')

    def test_complete_bundle_has_correct_manifest_checksums_and_summary(self):
        summary = self.base / 'summary.md'
        with patch.dict(os.environ, GITHUB_STEP_SUMMARY=str(summary), GITHUB_SHA='abc123'):
            self.collect()
        manifest = json.loads((self.output / 'manifest.json').read_text())
        self.assertEqual(7, len(manifest['packages']))
        self.assertEqual('abc123', manifest['commit'])
        checksums = (self.output / 'SHA256SUMS').read_text()
        for package in manifest['packages']:
            path = self.output / package['file']
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            self.assertEqual(digest, package['sha256'])
            self.assertEqual(path.stat().st_size, package['size_bytes'])
            self.assertIn(f"{digest}  {package['file']}", checksums)
        self.assertIn('client-feedback-packages', summary.read_text())

    def test_missing_or_duplicate_packages_fail_before_writing_output(self):
        path = next((self.inputs / 'rust-packages').glob('*.crate'))
        original = path.read_bytes()
        path.unlink()
        with self.assertRaisesRegex(ValueError, 'found 0'):
            self.collect()
        self.assertFalse(self.output.exists())
        path.write_bytes(original)
        path.with_name('artifactd-client-feedback-2.0.0.crate').write_bytes(original)
        with self.assertRaisesRegex(ValueError, 'found 2'):
            self.collect()
        self.assertFalse(self.output.exists())

    def test_corrupt_archives_and_wrong_payload_are_rejected(self):
        path = next((self.inputs / 'node-packages').glob('*.tgz'))
        path.write_bytes(b'broken')
        with self.assertRaises(tarfile.TarError):
            self.collect()
        self.assertFalse(self.output.exists())
        with tarfile.open(path, 'w:gz') as archive:
            info = tarfile.TarInfo('wrong-package.txt')
            info.size = 0
            archive.addfile(info, io.BytesIO())
        with self.assertRaisesRegex(ValueError, 'does not contain'):
            self.collect()
        self.assertFalse(self.output.exists())

    def test_php_archive_requires_composer_manifest_at_root(self):
        path = next((self.inputs / 'php-packages').glob('*.zip'))
        with zipfile.ZipFile(path, 'w') as archive:
            archive.writestr('src/ClientFeedbackClient.php', '<?php')
        with self.assertRaisesRegex(ValueError, 'Composer package manifest'):
            self.collect()
        self.assertFalse(self.output.exists())

    def test_refuses_to_overwrite_existing_output(self):
        self.output.mkdir()
        keep = self.output / 'keep.txt'
        keep.write_text('existing output')
        with self.assertRaisesRegex(ValueError, 'must be empty'):
            self.collect()
        self.assertEqual('existing output', keep.read_text())


if __name__ == '__main__':
    unittest.main()
