#!/usr/bin/env python3
"""Frame-side update helper. Python stdlib only; runs as the signed-in user."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tempfile
import time
import urllib.request
import zipfile

REPOSITORY = "Blackskydk/FrameEarthVR"
PACKAGE = "com.frameearthvr.app"
ALLOWED_TOP = {"FrameEarthVR.exe", "FrameEarthVR_Data", "UnityPlayer.dll", "GameAssembly.dll",
               "MonoBleedingEdge", "UnityCrashHandler64.exe", "D3D12", "earthvr-version.txt"}
MAX_UNPACKED = 8 * 1024**3
HOME = Path.home()
CONFIG_DIR = HOME / ".local/share/FrameEarthVR-Updater"


def write_json(path, data):
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(data), encoding="utf-8")
    os.replace(temporary, path)


def file_hash(path):
    sha = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            sha.update(block)
    return "sha256:" + sha.hexdigest()


def release_asset(request):
    tag = request["version"]
    if not re.fullmatch(r"v?[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?", tag):
        raise ValueError("Invalid update version")
    url = f"https://api.github.com/repos/{REPOSITORY}/releases/tags/{tag}"
    query = urllib.request.Request(url, headers={"User-Agent": "FrameEarthVR-Updater",
                                                 "Accept": "application/vnd.github+json"})
    with urllib.request.urlopen(query, timeout=15) as response:
        release = json.load(response)
    if release["draft"] or release["tag_name"] != tag:
        raise ValueError("Release is not published")
    name = "FrameEarthVR.apk" if request["platform"] == "apk" else "FrameEarthVR-Windows.zip"
    for asset in release["assets"]:
        if asset["name"] == name and asset["state"] == "uploaded":
            if not re.fullmatch(r"sha256:[0-9a-f]{64}", asset.get("digest", "")):
                raise ValueError("Release has no SHA-256 digest")
            if asset["digest"] != request["digest"]:
                raise ValueError("Release digest changed; check again")
            return asset
    raise ValueError("Release has no matching platform download")


def cached_release_asset(request):
    # Store accepted publisher metadata outside the app-writable inbox. Repeated
    # service steps must not make repeated GitHub calls while a game is closing.
    receipts = CONFIG_DIR / "receipts"
    receipts.mkdir(mode=0o700, exist_ok=True)
    path = receipts / (request["id"] + ".json")
    key = {k: request[k] for k in ("version", "platform", "digest")}
    if path.is_file():
        cached = json.loads(path.read_text())
        if cached["key"] == key and time.time() - cached["time"] < 300:
            return cached["asset"]
    asset = release_asset(request)
    write_json(path, {"key": key, "time": time.time(), "asset": asset})
    return asset


def extract_windows(payload, destination):
    with zipfile.ZipFile(payload) as archive:
        entries = archive.infolist()
        if len(entries) > 50000 or sum(e.file_size for e in entries) > MAX_UNPACKED:
            raise ValueError("Windows update is too large")
        seen = set()
        for entry in entries:
            name = entry.orig_filename
            parts = PurePosixPath(name).parts
            if (not parts or "\x00" in name or "\\" in name or ":" in name or name.startswith("/") or
                    any(p in ("", ".", "..") or p.endswith((" ", ".")) for p in name.rstrip("/").split("/")) or
                    parts[0] not in ALLOWED_TOP or any(p.lower().endswith(".local.json") for p in parts) or
                    (entry.external_attr >> 16) & 0o170000 == 0o120000):
                raise ValueError("Unsafe path in Windows update")
            normalized = name.rstrip("/").lower()
            if normalized in seen:
                raise ValueError("Duplicate path in Windows update")
            seen.add(normalized)
        required = {"frameearthvr.exe", "unityplayer.dll"}
        if not required.issubset(seen) or not {"frameearthvr_data/globalgamemanagers", "frameearthvr_data/data.unity3d"}.intersection(seen):
            raise ValueError("Windows update is incomplete")
        archive.extractall(destination)


def apply_windows(payload, root):
    # Back up only known game files. User configuration lives outside this folder.
    backup = root / ".earthvr-previous"
    with tempfile.TemporaryDirectory(prefix=".earthvr-stage-", dir=root.parent) as temporary:
        staging = Path(temporary)
        extract_windows(payload, staging)
        if backup.is_symlink():
            raise ValueError("Unsafe backup folder")
        if backup.exists():
            shutil.rmtree(backup)
        backup.mkdir()
        moved_old, moved_new = [], []
        try:
            for name in sorted(ALLOWED_TOP):
                target = root / name
                if target.exists():
                    os.replace(target, backup / name)
                    moved_old.append(name)
            for child in staging.iterdir():
                os.replace(child, root / child.name)
                moved_new.append(child.name)
        except Exception:
            for name in moved_new:
                target = root / name
                if target.is_dir():
                    shutil.rmtree(target)
                else:
                    target.unlink()
            for name in moved_old:
                os.replace(backup / name, root / name)
            raise


def apply_apk(payload, root):
    apk = root / "FrameEarthVR.apk"
    backup = root / ".earthvr-previous-apk"
    staged = root / ".earthvr-new-apk"
    if any(path.is_symlink() for path in (apk, backup, staged)):
        raise ValueError("APK install or backup path is a symlink")
    shutil.copyfile(payload, staged)
    # Flush before the atomic replacement, keeping the old APK available for recovery.
    with staged.open("r+b") as stream:
        os.fsync(stream.fileno())
    shutil.copyfile(apk, backup)
    os.replace(staged, apk)
    # Lepton versions also compare mtimes when deciding to refresh their bake.
    os.utime(apk, None)


def container_running(config):
    if config["platform"] != "apk":
        # Wine/FEX can map the game files in a process other than its launcher.
        # Check mapped binaries, not just the launcher's PID or a Unity heartbeat.
        binaries = [str(Path(config["install"]) / name) for name in ("FrameEarthVR.exe", "UnityPlayer.dll")]
        for process in Path("/proc").iterdir():
            if not process.name.isdigit():
                continue
            try:
                if process.stat().st_uid != os.getuid():
                    continue
                maps = (process / "maps").read_text()
                if any(binary in maps for binary in binaries):
                    return True
            except FileNotFoundError:
                continue
            except PermissionError:
                # Unrelated sandboxed processes may hide their maps. Only block
                # on one whose command line identifies this game.
                try:
                    if b"FrameEarthVR.exe" in (process / "cmdline").read_bytes():
                        return True
                except (PermissionError, FileNotFoundError):
                    continue
        return False
    output = subprocess.check_output(["podman", "ps", "--format", "{{.Names}}"], text=True, timeout=10)
    return config["container"] in output.splitlines()


def process_update(config, lookup=release_asset, running=container_running, wait=time.sleep, defer=False):
    inbox = Path(config["inbox"])
    request_path = inbox / "request.json"
    if not request_path.is_file():
        return
    request = json.loads(request_path.read_text(encoding="utf-8-sig"))
    result_path = inbox / "result.json"
    identity = request.get("id", "")
    if not re.fullmatch(r"[0-9a-f]{32}", identity):
        raise ValueError("Invalid update request ID")
    pending = False
    try:
        if request.get("schema") != 1 or request.get("platform") != config["platform"]:
            raise ValueError("Updater platform mismatch")
        asset = lookup(request)  # Independently verify against the publisher's release feed.
        payload = inbox / ("payload.apk" if config["platform"] == "apk" else "payload.zip")
        if payload.is_symlink() or payload.stat().st_size != asset["size"] or file_hash(payload) != asset["digest"]:
            raise ValueError("Update download failed verification")
        configured_root = Path(config["install"])
        root = configured_root.resolve(strict=True)
        if configured_root.is_symlink() or root == HOME or root == Path("/"):
            raise ValueError("Invalid install folder")
        if config["platform"] == "windows":
            # Validate the ZIP before telling the game it is safe to exit.
            with tempfile.TemporaryDirectory(dir=root.parent) as temporary:
                extract_windows(payload, Path(temporary))
        write_json(result_path, {"id": identity, "state": "ready", "message": "Close game to apply update"})
        deadline = time.monotonic() + 120
        while True:
            if not request_path.exists():
                raise ValueError("Update request was cancelled")
            heartbeat = inbox / "game-running"
            quiet = not heartbeat.exists() or time.time() - heartbeat.stat().st_mtime > 6
            if quiet and not running(config):
                break
            if defer:
                pending = True
                return
            if time.monotonic() > deadline:
                raise ValueError("Game did not exit; update was not installed")
            wait(1)
        if config["platform"] == "apk":
            if file_hash(payload) != asset["digest"]:
                raise ValueError("Update payload changed while waiting")
            apply_apk(payload, root)
        else:
            if file_hash(payload) != asset["digest"]:
                raise ValueError("Update payload changed while waiting")
            apply_windows(payload, root)
        write_json(result_path, {"id": identity, "state": "installed", "message": "Update installed; reopen from Steam"})
    except Exception as error:
        write_json(result_path, {"id": identity, "state": "error", "message": str(error)})
    finally:
        if not pending:
            request_path.unlink(missing_ok=True)


def serve():
    configs = json.loads((CONFIG_DIR / "config.json").read_text())
    while True:
        for config in configs:
            try:
                is_running = container_running(config)
                if config["platform"] == "apk":
                    # Android-owned files in rootless Lepton use subordinate
                    # UIDs. Access them in podman's user namespace, without sudo.
                    subprocess.run(["podman", "unshare", "/usr/bin/python3", str(Path(__file__).resolve()),
                                    "--step", json.dumps(config), "--running", str(int(is_running))],
                                   check=True, timeout=90)
                else:
                    step(config, is_running)
            except Exception as error:
                print(f"Updater error: {error}", flush=True)
        time.sleep(2)


def step(config, is_running):
    inbox = Path(config["inbox"])
    inbox.mkdir(parents=True, exist_ok=True)
    write_json(inbox / "bridge.json", {"schema": 1, "platform": config["platform"]})
    process_update(config, lookup=cached_release_asset, running=lambda _: is_running, defer=True)


def discover_apk_data():
    return [c for library in steam_libraries() if (library / "compatdata").is_dir()
            for c in (library / "compatdata").iterdir() if c.name.isdigit() and
            (c / "external/Android/data" / PACKAGE / "files").is_dir()]


def steam_libraries():
    roots = [HOME / ".local/share/Steam", HOME / ".steam/steam"]
    libraries = set()
    for root in roots:
        if root.is_dir():
            libraries.add(root.resolve() / "steamapps")
        folders = root / "steamapps/libraryfolders.vdf"
        if folders.is_file():
            for value in re.findall(r'"path"\s+"([^"]+)"', folders.read_text()):
                libraries.add(Path(value.replace("\\\\", "\\")) / "steamapps")
    return sorted(libraries)


def choose_path(prompt, candidates):
    candidates = sorted(set(p.resolve() for p in candidates))
    if len(candidates) == 1:
        print(f"Found {prompt}: {candidates[0]}")
        return candidates[0]
    for index, path in enumerate(candidates, 1):
        print(f"  {index}. {path}")
    answer = input(prompt + " (number above or drag/paste folder): ").strip().strip("'\"")
    if answer.isdigit() and 1 <= int(answer) <= len(candidates):
        return candidates[int(answer) - 1]
    return Path(answer).expanduser().resolve(strict=True)


def configure_build(platform):
    expected = "FrameEarthVR.apk" if platform == "apk" else "FrameEarthVR.exe"
    libraries = steam_libraries()
    search_roots = [HOME / "devkit-game"] + [p / "common" for p in libraries]
    installs = [p.parent for root in search_roots if root.is_dir() for p in root.glob("*/" + expected)]
    install = choose_path("Installed game folder", installs)
    if not (install / expected).is_file() or install == HOME or install == Path("/"):
        raise SystemExit(f"Choose the game folder containing {expected}")
    suffix = "external/Android/data/" + PACKAGE + "/files" if platform == "apk" else "pfx/drive_c/users/*/AppData/LocalLow/DefaultCompany/FrameEarthVR"
    if platform == "apk":
        discovery = subprocess.check_output(["podman", "unshare", "/usr/bin/python3", str(Path(__file__).resolve()),
                                             "--discover-apk"], text=True, timeout=20)
        compat_candidates = [Path(p) for p in json.loads(discovery)]
    else:
        compat_candidates = [c for library in libraries if (library / "compatdata").is_dir()
                             for c in (library / "compatdata").iterdir() if c.name.isdigit() and any(c.glob(suffix))]
    compat = choose_path("Game's Steam compatdata folder", compat_candidates)
    if not compat.name.isdigit():
        raise SystemExit("Choose the numeric folder for this game's shortcut under steamapps/compatdata")
    if platform == "apk":
        persistent = compat / "external/Android/data" / PACKAGE / "files"
        if not shutil.which("podman"):
            raise SystemExit("Lepton's podman tool was not found")
    else:
        candidates = list((compat / "pfx/drive_c/users").glob("*/AppData/LocalLow/DefaultCompany/FrameEarthVR"))
        if len(candidates) != 1:
            raise SystemExit("Could not uniquely find the game's Proton save folder; launch it once first")
        persistent = candidates[0]
    exists = subprocess.run(["podman", "unshare", "test", "-d", str(persistent)]).returncode == 0 if platform == "apk" else persistent.is_dir()
    if not exists:
        raise SystemExit("Game's data folder is missing; check the compatdata folder and launch the game once")
    inbox = persistent / "EarthVR/Updates"
    if platform == "apk":
        if subprocess.run(["podman", "unshare", "test", "-d", str(inbox)]).returncode != 0:
            raise SystemExit("Launch preview.3 or later once to create its updater folder, then close it and retry")
    else:
        inbox.mkdir(parents=True, exist_ok=True)
    return {"platform": platform, "install": str(install), "inbox": str(inbox),
            "container": "lepton-steamlaunch-" + compat.name}


def setup():
    if os.name != "posix" or not shutil.which("systemctl"):
        raise SystemExit("Run --setup in a terminal on your Frame, not on Windows.")
    print("Frame Earth VR updater setup. No administrator access or terrain token needed.")
    print("First install and launch the game once using Devkit Client, then close it.")
    platform = input("Builds to configure (apk/windows/both): ").strip().lower()
    if platform not in ("apk", "windows", "both"):
        raise SystemExit("Choose apk, windows, or both")
    CONFIG_DIR.mkdir(mode=0o700, parents=True, exist_ok=True)
    config_path = CONFIG_DIR / "config.json"
    configs = json.loads(config_path.read_text()) if config_path.exists() else []
    for kind in (("apk", "windows") if platform == "both" else (platform,)):
        print(f"Configuring {kind} build…")
        entry = configure_build(kind)
        configs = [c for c in configs if c["platform"] != kind] + [entry]
    write_json(config_path, configs)
    script = CONFIG_DIR / "frame-updater.py"
    if Path(__file__).resolve() != script.resolve():
        shutil.copyfile(__file__, script)
    service_dir = HOME / ".config/systemd/user"
    service_dir.mkdir(parents=True, exist_ok=True)
    # systemd's quoting handles paths with spaces; percent must be escaped for specifiers.
    command = str(script).replace("%", "%%").replace("\\", "\\\\").replace('"', '\\"')
    (service_dir / "frameearthvr-updater.service").write_text(
        '[Unit]\nDescription=Frame Earth VR update helper\n'
        '[Service]\nExecStart=/usr/bin/python3 "' + command + '" --serve\nRestart=on-failure\n'
        '[Install]\nWantedBy=default.target\n')
    subprocess.run(["systemctl", "--user", "daemon-reload"], check=True)
    subprocess.run(["systemctl", "--user", "enable", "frameearthvr-updater.service"], check=True)
    subprocess.run(["systemctl", "--user", "restart", "frameearthvr-updater.service"], check=True)
    print("Ready. Future updates download in the game and apply after it closes. Reopen from Steam.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--setup", action="store_true")
    parser.add_argument("--serve", action="store_true")
    parser.add_argument("--step")
    parser.add_argument("--running", type=int, default=1)
    parser.add_argument("--discover-apk", action="store_true")
    arguments = parser.parse_args()
    if arguments.setup:
        setup()
    elif arguments.serve:
        serve()
    elif arguments.step:
        step(json.loads(arguments.step), bool(arguments.running))
    elif arguments.discover_apk:
        print(json.dumps([str(p) for p in discover_apk_data()]))
    else:
        parser.print_help()
