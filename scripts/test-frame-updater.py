"""Exercise the actual updater on temporary install folders, never a real game."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location("frame_updater", Path(__file__).with_name("frame-updater.py"))
updater = importlib.util.module_from_spec(spec)
spec.loader.exec_module(updater)


class UpdaterTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.root = self.base / "game"
        self.root.mkdir()
        self.inbox = self.base / "data/EarthVR/Updates"
        self.inbox.mkdir(parents=True)

    def archive(self, extra=None):
        payload = self.inbox / "payload.zip"
        with zipfile.ZipFile(payload, "w") as archive:
            for name in ("FrameEarthVR.exe", "UnityPlayer.dll", "FrameEarthVR_Data/globalgamemanagers"):
                archive.writestr(name, b"new")
            if extra:
                archive.writestr(extra, b"bad")
        if extra and "\\" in extra:
            # Windows' zip writer normalizes backslashes; craft the hostile ZIP name explicitly.
            payload.write_bytes(payload.read_bytes().replace(extra.replace("\\", "/").encode(), extra.encode()))
        return payload

    def test_windows_replacement_preserves_user_files(self):
        (self.root / "FrameEarthVR.exe").write_bytes(b"old")
        (self.root / "my-file.txt").write_text("keep")
        updater.apply_windows(self.archive(), self.root)
        self.assertEqual((self.root / "FrameEarthVR.exe").read_bytes(), b"new")
        self.assertEqual((self.root / ".earthvr-previous/FrameEarthVR.exe").read_bytes(), b"old")
        self.assertEqual((self.root / "my-file.txt").read_text(), "keep")

    def test_windows_rollback_after_move_failure(self):
        (self.root / "FrameEarthVR.exe").write_bytes(b"old")
        payload = self.archive()
        original = updater.os.replace
        failed = False
        def replace(source, destination):
            nonlocal failed
            if not failed and Path(destination) == self.root / "UnityPlayer.dll":
                failed = True
                raise OSError("simulated disk error")
            original(source, destination)
        with patch.object(updater.os, "replace", side_effect=replace):
            with self.assertRaises(OSError):
                updater.apply_windows(payload, self.root)
        self.assertEqual((self.root / "FrameEarthVR.exe").read_bytes(), b"old")

    def test_unsafe_zip_paths_rejected(self):
        for name in ("../escape", "/absolute", "FrameEarthVR_Data/../escape", "FrameEarthVR_Data/file:stream",
                     "FrameEarthVR_Data\\escape", "other.exe", "FrameEarthVR_Data/token.local.json",
                     "FrameEarthVR_Data/name.", "FrameEarthVR_Data//empty"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as destination:
                with self.assertRaises(ValueError):
                    updater.extract_windows(self.archive(name), Path(destination))

    def test_duplicate_zip_paths_rejected(self):
        with tempfile.TemporaryDirectory() as destination:
            with self.assertRaises(ValueError):
                updater.extract_windows(self.archive("frameearthvr.exe"), Path(destination))

    def test_symlink_zip_entry_rejected(self):
        payload = self.archive()
        with zipfile.ZipFile(payload, "a") as archive:
            entry = zipfile.ZipInfo("FrameEarthVR_Data/link")
            entry.external_attr = (0o120777 << 16)
            archive.writestr(entry, "outside")
        with tempfile.TemporaryDirectory() as destination:
            with self.assertRaises(ValueError):
                updater.extract_windows(payload, Path(destination))

    def test_incomplete_zip_rejected(self):
        payload = self.inbox / "payload.zip"
        with zipfile.ZipFile(payload, "w") as archive:
            archive.writestr("FrameEarthVR.exe", b"new")
        with self.assertRaises(ValueError):
            updater.apply_windows(payload, self.root)
        self.assertFalse((self.root / ".earthvr-previous").exists())

    def test_apk_replaced_without_touching_data(self):
        (self.root / "FrameEarthVR.apk").write_bytes(b"old")
        payload = self.inbox / "payload.apk"
        payload.write_bytes(b"new")
        updater.apply_apk(payload, self.root)
        self.assertEqual((self.root / "FrameEarthVR.apk").read_bytes(), b"new")
        self.assertEqual((self.root / ".earthvr-previous-apk").read_bytes(), b"old")
        self.assertEqual(len(list(self.root.glob("*.apk"))), 1)

    def request(self):
        (self.root / "FrameEarthVR.apk").write_bytes(b"old")
        payload = self.inbox / "payload.apk"
        payload.write_bytes(b"new")
        request = dict(schema=1, id="a" * 32, version="v1.0.0-preview.4", platform="apk", digest=updater.file_hash(payload))
        updater.write_json(self.inbox / "request.json", request)
        return dict(install=str(self.root), inbox=str(self.inbox), platform="apk"), dict(size=3, digest=request["digest"])

    def test_handoff_and_install(self):
        config, asset = self.request()
        updater.process_update(config, lookup=lambda _: asset, running=lambda _: False)
        result = json.loads((self.inbox / "result.json").read_text())
        self.assertEqual(result["state"], "installed")
        self.assertFalse((self.inbox / "request.json").exists())

    def test_bad_digest_leaves_installed_apk(self):
        config, asset = self.request()
        asset["digest"] = "sha256:" + "0" * 64
        updater.process_update(config, lookup=lambda _: asset, running=lambda _: False)
        self.assertEqual((self.root / "FrameEarthVR.apk").read_bytes(), b"old")
        self.assertEqual(json.loads((self.inbox / "result.json").read_text())["state"], "error")

    def test_no_install_until_container_exits(self):
        config, asset = self.request()
        checks = iter((True, False))
        observed = []
        updater.process_update(config, lookup=lambda _: asset, running=lambda _: next(checks),
                               wait=lambda _: observed.append((self.root / "FrameEarthVR.apk").read_bytes()))
        self.assertEqual(observed, [b"old"])
        self.assertEqual((self.root / "FrameEarthVR.apk").read_bytes(), b"new")

    def test_feed_failure_leaves_game_untouched(self):
        config, _ = self.request()
        def fail(_):
            raise OSError("offline")
        updater.process_update(config, lookup=fail)
        self.assertEqual((self.root / "FrameEarthVR.apk").read_bytes(), b"old")

    def test_deferred_handoff_keeps_request_until_game_exits(self):
        config, asset = self.request()
        updater.process_update(config, lookup=lambda _: asset, running=lambda _: True, defer=True)
        self.assertTrue((self.inbox / "request.json").exists())
        self.assertEqual(json.loads((self.inbox / "result.json").read_text())["state"], "ready")
        self.assertEqual((self.root / "FrameEarthVR.apk").read_bytes(), b"old")
        updater.process_update(config, lookup=lambda _: asset, running=lambda _: False, defer=True)
        self.assertFalse((self.inbox / "request.json").exists())
        self.assertEqual((self.root / "FrameEarthVR.apk").read_bytes(), b"new")


if __name__ == "__main__":
    unittest.main()
