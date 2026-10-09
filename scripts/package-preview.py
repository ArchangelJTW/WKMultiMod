#!/usr/bin/env python3
"""Package a previously built mod for manual BepInEx installation/testing."""

import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "bin" / "Release"
RESOURCES = ROOT / "resources" / "1.8.3"
BUNDLE_SHA256 = "30527fffcc7a5e7219c53779ae80ea4be6a96f207e6ddd4648c4f07419723f3d"


def main():
    version = ET.parse(ROOT / "src/Core/WKMultiPlayerMod.csproj").findtext("PropertyGroup/Version")
    if not version or any(part not in "0123456789." for part in version):
        raise SystemExit("Could not read the mod version from the Core project.")

    files = {
        "MP/WKMultiPlayerMod.dll": OUTPUT / "WKMultiPlayerMod.dll",
        "MP/WKMultiPlayerMod.Shared.dll": OUTPUT / "WKMultiPlayerMod.Shared.dll",
        "MP/texts_en.json": OUTPUT / "texts_en.json",
        "MP/texts_zh.json": OUTPUT / "texts_zh.json",
        "MP/player_prefab": RESOURCES / "player_prefab",
        "MP/grub.png": RESOURCES / "grub.png",
        "MP/hang.png": RESOURCES / "hang.png",
        "TESTING.md": ROOT / "docs/reconnect-and-checkpoints.md",
        "saved-runs.md": ROOT / "docs/saved-runs.md",
    }
    missing = [str(path.relative_to(ROOT)) for path in files.values() if not path.is_file()]
    if missing:
        raise SystemExit("Missing files; build Release first: " + ", ".join(missing))
    payload = {name: path.read_bytes() for name, path in files.items()}
    if hashlib.sha256(payload["MP/player_prefab"]).hexdigest() != BUNDLE_SHA256:
        raise SystemExit("The player bundle does not match the verified upstream 1.8.3 bundle.")
    for name in ("MP/texts_en.json", "MP/texts_zh.json"):
        json.loads(payload[name])
    commit = subprocess.check_output(
        ["git", "describe", "--always", "--dirty", "--abbrev=12"], cwd=ROOT, text=True
    ).strip()
    payload["README.txt"] = f"""WKMultiMod {version} - networking/checkpoint/native-save preview
Source commit: {commit}
Source: https://github.com/ArchangelJTW/WKMultiMod/tree/dev

TEST BUILD: compiles and has automated policy tests, but still needs a
real two-player Unity/Steam test. Use a disposable run and back up saves.
Both players must use this same build. Checkpoint protocol 2 includes the
persistent run ID; do not mix 1.8.3.5 and 1.8.3.6 checkpoint peers.

INSTALL (requires the working BepInEx installation you already use):
1. Completely close White Knuckle.
2. Back up your existing BepInEx/plugins/MP folder somewhere OUTSIDE
   BepInEx/plugins, so the backup DLLs are not loaded too.
3. Copy this ZIP's MP folder into BepInEx/plugins and replace all files
   in the existing MP folder. Replace both DLLs and player_prefab together.
4. Do not install a second copy of the multiplayer DLLs elsewhere in plugins.
5. Start the game. Look for mod version {version} in BepInEx/LogOutput.log.

NEXT-DAY CONTINUATION:
Host: use Continue Multiplayer on the ordinary saved game-mode screen.
Fallback: use ordinary Continue, wait for the world to load, then run
host MyRun friends 8. Hosting no longer restarts the loaded run.
Guests: join the NEW lobby ID. A matching save made with this build loads
YOUR OWN native saved inventory; the host's equipment is never copied.
Old saves can be hosted, but old guest inventories are not auto-guessed
by seed. Both players must save/exit normally on their own computers.
See saved-runs.md for upgrade behavior, limitations and the full checklist.

WHAT TO TEST:
- allplayer: real local position and a synced remote model/pose; no endless
  empty member-data/missing-player loop. Unknown poses say not synced yet.
- Late join: host reaches a checkpoint, then walks past it. Join at the
  checkpoint (not the host's current position), keeping your own items.
- Resumed interlude: the guest must load the host's actual interlude terrain,
  not appear nearby in the first area. Look for Loading host level and Joined
  at checkpoint messages. Failed placement now refuses multiplayer admission.
- Logs now say which join path ran and why the host chose Normal instead
  of checkpoint placement (no checkpoint, different route, not later, disabled).
- New host run in the SAME lobby/seed: old-world syncing must stop; reconnect
  must not reuse a prior run's world, checkpoint or pending-death state.
- Save metadata: no Invalid saved item history warnings after ordinary pickups.
- Live reconnect: keep your own position and inventory after network recovery.
- Death/restart or death/rejoin: if the host has a later checkpoint on the
  same route, spawn there with empty hands/bag/pouches/pockets/artifacts.
- Equal/earlier/different-route host checkpoint: use normal respawn instead.

See TESTING.md and saved-runs.md for limitations, settings and checklists.
If something fails, collect BOTH players' BepInEx/LogOutput.log files
before restarting the game (the log may be overwritten on launch).

ROLL BACK:
Close the game, remove the preview MP folder and restore your backed-up
working MP folder. Do not overwrite your saves with another player's saves.

The original player bundle/icons are by the upstream mod author, Shen-X-L.
No game DLLs, BepInEx files, personal configurations or saves are included.
""".encode("utf-8")
    payload["SHA256SUMS"] = "".join(
        f"{hashlib.sha256(data).hexdigest()}  {name}\n" for name, data in sorted(payload.items())
    ).encode("ascii")
    archive = OUTPUT / f"WKMultiMod-{version}-preview.zip"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as package:
        for name, data in sorted(payload.items()):
            package.writestr(name, data)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_suffix(".zip.sha256").write_text(f"{digest}  {archive.name}\n", encoding="ascii")
    print(f"Package: {archive}")
    print(f"Source: {commit}")
    print(f"Size: {archive.stat().st_size:,} bytes")
    print(f"SHA-256: {digest}")


if __name__ == "__main__":
    main()
