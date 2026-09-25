import pathlib
import tempfile
import unittest
import urllib.error
from unittest import mock

import importlib.util

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("caddy_release", ROOT / "scripts/check-caddy-image-release.py")
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader
SPEC.loader.exec_module(MODULE)


class CaddyImageReleaseTests(unittest.TestCase):
    def make_root(self, version="2.11.4-1", compose_version="2.11.4-1"):
        temporary = tempfile.TemporaryDirectory()
        root = pathlib.Path(temporary.name)
        (root / ".github/workflows").mkdir(parents=True)
        (root / ".github/workflows/caddy-image.yml").write_text(
            f"env:\n  IMAGE: hsmonitoring/hsm-caddy\n  VERSION: {version}\n", encoding="utf-8"
        )
        (root / "docker-compose.yml").write_text(
            f"services:\n  caddy:\n    image: 'hsmonitoring/hsm-caddy:{compose_version}'\n", encoding="utf-8"
        )
        self.addCleanup(temporary.cleanup)
        return root

    def test_workflow_version_matches_reference_compose(self):
        self.assertEqual(MODULE.release_coordinates(self.make_root()), ("hsmonitoring/hsm-caddy", "2.11.4-1"))

    def test_mismatched_compose_tag_fails(self):
        with self.assertRaisesRegex(ValueError, "must reference"):
            MODULE.release_coordinates(self.make_root(compose_version="2.11.4-2"))

    @mock.patch.object(MODULE.urllib.request, "urlopen")
    def test_existing_registry_tag_fails(self, urlopen):
        response = mock.MagicMock()
        response.__enter__.return_value.status = 200
        urlopen.return_value = response
        with self.assertRaisesRegex(RuntimeError, "already exists"):
            MODULE.assert_tag_absent("hsmonitoring/hsm-caddy", "2.11.4-1")

    @mock.patch.object(MODULE.urllib.request, "urlopen")
    def test_missing_registry_tag_is_available(self, urlopen):
        urlopen.side_effect = urllib.error.HTTPError("url", 404, "missing", {}, None)
        MODULE.assert_tag_absent("hsmonitoring/hsm-caddy", "2.11.4-1")

    @mock.patch.object(MODULE.urllib.request, "urlopen")
    def test_registry_error_fails_closed(self, urlopen):
        urlopen.side_effect = urllib.error.URLError("offline")
        with self.assertRaisesRegex(RuntimeError, "refusing to publish"):
            MODULE.assert_tag_absent("hsmonitoring/hsm-caddy", "2.11.4-1")

    @mock.patch.object(MODULE.urllib.request, "urlopen")
    def test_registry_http_errors_fail_closed(self, urlopen):
        for status in (403, 429, 500):
            with self.subTest(status=status):
                urlopen.side_effect = urllib.error.HTTPError("url", status, "registry error", {}, None)
                with self.assertRaisesRegex(RuntimeError, f"HTTP {status}.*refusing to publish"):
                    MODULE.assert_tag_absent("hsmonitoring/hsm-caddy", "2.11.4-1")

    @mock.patch.object(MODULE.urllib.request, "urlopen")
    def test_unexpected_registry_status_fails_closed(self, urlopen):
        response = mock.MagicMock()
        response.__enter__.return_value.status = 202
        urlopen.return_value = response
        with self.assertRaisesRegex(RuntimeError, "unexpected HTTP 202"):
            MODULE.assert_tag_absent("hsmonitoring/hsm-caddy", "2.11.4-1")


if __name__ == "__main__":
    unittest.main()
