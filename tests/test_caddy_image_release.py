import pathlib
import subprocess
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

    def test_unpublished_tag_is_published(self):
        self.assertTrue(MODULE.plan_publish("hsmonitoring/hsm-caddy", "2.11.4-1", published=False, unbumped_change=True))

    def test_published_tag_without_unbumped_change_is_skipped(self):
        self.assertFalse(MODULE.plan_publish("hsmonitoring/hsm-caddy", "2.11.4-1", published=True, unbumped_change=False))

    def test_published_tag_with_unbumped_change_demands_version_bump(self):
        with self.assertRaisesRegex(RuntimeError, "bump VERSION"):
            MODULE.plan_publish("hsmonitoring/hsm-caddy", "2.11.4-1", published=True, unbumped_change=True)

    def make_repo(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = pathlib.Path(temporary.name)
        self.git(root, "init", "-q", "-b", "master")
        (root / "caddy").mkdir()
        (root / ".github/workflows").mkdir(parents=True)
        return root

    @staticmethod
    def git(root, *args):
        subprocess.run(
            ["git", "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", *args],
            cwd=root, check=True, capture_output=True,
        )

    def commit(self, root, message, caddy=None, version=None, compose=None):
        if caddy is not None:
            (root / "caddy/Caddyfile").write_text(caddy, encoding="utf-8")
        if version is not None:
            (root / ".github/workflows/caddy-image.yml").write_text(f"env:\n  VERSION: {version}\n", encoding="utf-8")
        if compose is not None:
            (root / "docker-compose.yml").write_text(compose, encoding="utf-8")
        self.git(root, "add", "-A")
        self.git(root, "commit", "-q", "-m", message)

    def test_unbumped_context_change_follows_master_history(self):
        root = self.make_repo()
        self.commit(root, "introduce image", caddy="a", version="1")
        self.assertFalse(MODULE.unbumped_context_change("1", root))
        self.commit(root, "edit caddy only", caddy="b")
        self.assertTrue(MODULE.unbumped_context_change("1", root))
        self.commit(root, "edit compose only", compose="x")
        self.assertTrue(MODULE.unbumped_context_change("1", root), "a later unrelated push must not hide the change")
        self.commit(root, "bump", version="2")
        self.assertFalse(MODULE.unbumped_context_change("2", root))
        self.commit(root, "edit compose again", compose="y")
        self.assertFalse(MODULE.unbumped_context_change("2", root))

    def test_returning_to_an_older_version_is_not_a_bump(self):
        root = self.make_repo()
        self.commit(root, "introduce image", caddy="a", version="2.11.4-1")
        self.commit(root, "bump", caddy="b", version="2.11.4-2")
        self.commit(root, "edit caddy and go back", caddy="c", version="2.11.4-1")
        self.assertTrue(MODULE.unbumped_context_change("2.11.4-1", root))

    def test_version_prefix_does_not_match_a_longer_version(self):
        root = self.make_repo()
        self.commit(root, "introduce image", caddy="a", version="2.11.4-20")
        self.commit(root, "edit caddy", caddy="b")
        self.commit(root, "bump", version="2.11.4-2")
        self.assertFalse(MODULE.unbumped_context_change("2.11.4-2", root))

    def test_pr_that_bumps_before_editing_caddy_counts_as_bumped(self):
        root = self.make_repo()
        self.commit(root, "introduce image", caddy="a", version="1")
        self.git(root, "switch", "-q", "-c", "feature")
        self.commit(root, "bump first", version="2")
        self.commit(root, "then edit caddy", caddy="b")
        self.git(root, "switch", "-q", "master")
        self.git(root, "merge", "-q", "--no-ff", "-m", "merge feature", "feature")
        self.assertFalse(MODULE.unbumped_context_change("2", root))

    def test_shallow_checkout_fails_closed(self):
        source = self.make_repo()
        self.commit(source, "introduce image", caddy="a", version="1")
        self.commit(source, "edit caddy only", caddy="b")
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        clone = pathlib.Path(temporary.name) / "clone"
        self.git(temporary.name, "clone", "-q", "--depth=1", source.as_uri(), str(clone))
        with self.assertRaisesRegex(RuntimeError, "shallow"):
            MODULE.unbumped_context_change("1", clone)

    def test_missing_history_fails_closed(self):
        root = self.make_repo()
        self.commit(root, "compose only", compose="x")
        with self.assertRaisesRegex(RuntimeError, "refusing to decide"):
            MODULE.unbumped_context_change("1", root)

    def run_plan(self, published, unbumped, *flags):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        output = pathlib.Path(temporary.name) / "github_output"
        output.touch()
        with (
            mock.patch.object(MODULE, "release_coordinates", return_value=("hsmonitoring/hsm-caddy", "2.11.4-1")),
            mock.patch.object(MODULE, "tag_published", return_value=published),
            mock.patch.object(MODULE, "unbumped_context_change", return_value=unbumped) as unbumped_check,
            mock.patch.object(MODULE.sys, "argv", ["check", "--plan", *flags]),
            mock.patch.dict(MODULE.os.environ, {"GITHUB_OUTPUT": str(output)}),
        ):
            code = MODULE.main()
        return code, output.read_text(encoding="utf-8"), unbumped_check

    def test_plan_writes_the_publish_output(self):
        self.assertEqual(self.run_plan(False, True)[:2], (0, "publish=true\n"))
        self.assertEqual(self.run_plan(True, False)[:2], (0, "publish=false\n"))

    def test_plan_never_publishes_with_no_publish(self):
        self.assertEqual(self.run_plan(False, True, "--no-publish")[:2], (0, "publish=false\n"))
        self.assertEqual(self.run_plan(True, True, "--no-publish")[:2], (1, ""))

    def test_plan_skips_history_check_for_unpublished_tag(self):
        self.run_plan(False, True)[2].assert_not_called()

    def test_plan_fails_without_output_on_unbumped_change(self):
        self.assertEqual(self.run_plan(True, True)[:2], (1, ""))


if __name__ == "__main__":
    unittest.main()
