import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("php_package", ROOT / "ci/package-php.py")
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


class PhpPackageTests(unittest.TestCase):
    def test_archive_is_installable_and_excludes_checkout_dependencies(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            result = package.package(Path(directory))
            with zipfile.ZipFile(result) as archive:
                names = set(archive.namelist())
                self.assertIn("composer.json", names)
                self.assertIn("autoload.php", names)
                self.assertIn("src/ClientFeedbackClient.php", names)
                self.assertIn("README.md", names)
                self.assertTrue(all(name.endswith(".php") for name in names if name.startswith("src/")))
                self.assertFalse(any(name.startswith(("vendor/", "tests/", "dist/")) for name in names))
                metadata = json.loads(archive.read("composer.json"))
                self.assertEqual("artifactd/client-feedback", metadata["name"])
                self.assertEqual((ROOT / "VERSION").read_text().strip(), metadata["version"])
