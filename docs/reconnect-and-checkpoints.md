# Reconnect and host-checkpoint preview — 1.8.3.3

Based on upstream `dev` commit `af7e782555f96ec72e4775df623299079c46d786`.
Uses the unchanged working 1.8.3 resources in `resources/1.8.3/`.

**Implemented and build-tested; not yet validated in the game with two players.**
Both computers should install the preview. Never load it alongside another copy
of the multiplayer DLLs. The installed game files are not modified by building.

## Gameplay rules

| Situation | Spawn | Items |
| --- | --- | --- |
| Connection loss while alive; same world still loaded | Keep your own position | Keep your own inventory |
| First-time/late join | Host's latest reached checkpoint, if available on the same route | Keep your own starting items; never copy host equipment |
| Death, with host checkpoint further along the same route | Advance to that host checkpoint | Empty hands, backpack, normal/extra pouches and pockets, including artifacts |
| Death, host checkpoint equal/earlier/unrelated or unavailable | Normal respawn | Normal game rules; no restoration of pre-death inventory |
| Different lobby, world seed or mode | Normal initialization | No old reconnect state |

The death rule also applies to dying, leaving and rejoining. A death tombstone
is kept across failed joins, scene reloads and returning to the menu within this
game process, but it cannot transfer to a different lobby/seed/mode. A failed
checkpoint request retains that tombstone and retries rather than being
misclassified as an alive reconnect. Join/restart auto-commands are skipped for
post-death spawns to avoid silently granting items again.

Normal death UI/game respawn is retained. Once the player respawns/restarts or
rejoins alive, the checkpoint rule is resolved automatically; it does not
resurrect the dead player or consume a host's saved life.

## Host settings

In `BepInEx/config/shenxl.MultiPlayerMod.cfg`, under `[LobbyRule]`:

```ini
JoinAtHostCheckpoint = true
RespawnAtHostCheckpoint = true
```

Both default to true. The host advertises protocol support and settings in the
lobby. Old hosts without this protocol fall back to normal initialization.

## Checkpoint implementation

- Captures actual `UT_TravelDistanceCheckpoint.SetCheckpoint` events and
  registered non-session respawn-save events. Periodic/session autosaves and
  the host's current walking position are not treated as checkpoints.
- Stores target level/region/seed, remaining generation list, level-relative
  pose, level/world transform and game progression. No host inventory, perks,
  saved lives, save-file contents, enemy saves or item saves are transmitted.
- Compares branch lineage and stable generation IDs, then travel progression
  within a level. Never compares wall-clock time or raw world-space Y.
- Repeated room names are resolved by their actual generation identity, not the
  first matching prefab name in history.
- Regenerates the target level with the game's `saveLoad` generation path,
  aligns the world root to the host's saved level transform, cycles colliders,
  and teleports only after the geometry is loaded.
- Updates cached checkpoint anchors when the host's world is offset/rebased.
- Deletes old dynamic network replicas, rebinds loaded entities/items, and
  requests fresh item tombstones/enemy deaths/climbable history after placement.
- Request/reply is direct-peer authenticated and bound to host, lobby, seed,
  canonical game-mode signature, scene revision and attempt token. Spoofed,
  forwarded, stale and unsolicited replies are ignored.
- Snapshot limits: 128 KiB compressed, 512 KiB decompressed UTF-8 JSON. The
  generation list is limited to 256 entries. Requests are rate-limited.

## Reconnect fixes included

- Entry initialization runs once whether Steam's callback or async join/create
  result arrives first. Both success paths explicitly finalize entry, publish
  member metadata and set the host ID; assigning a lobby handle is not used as
  a substitute for initialization.
- Direct member-data replies use a local profile cache instead of waiting for
  Steam to echo its member-data index. This fixes the empty metadata/missing
  player-model loop observed in the 1.8.3.2 preview log.
- `allplayer` shows the real local position and `not synced yet` for a remote
  player without an accepted pose, rather than reporting misleading zeros.
- Enemy death chunks are retained while the game is loading and applied after
  native world/player initialization. Errors include the full stack trace.
- Safe/idempotent peer cleanup when coroutine bookkeeping is already gone.
- Tracks and closes pending connections; ignores old-handle close callbacks.
- Scopes Steam callbacks and late async results to the intended lobby.
- A 15-second lobby-join timeout instead of a permanently stuck join state.
- One join path for console, browser buttons, and Steam invites.
- `reconnect`, or `join <same_lobby_id>`, works without manually leaving first.
- `leave` followed by rejoining the same still-loaded live world can resume it.
- Retains network identities to avoid duplicate objects on live reconnect.
- Requests fresh member/pose data immediately after recovery.

Existing peer retries still operate while Steam considers both players lobby
members. If Steam removed membership, use `reconnect` after connectivity returns.

## Limits

This is **not a persistent autosave**. Quitting the process invalidates recovery
and death tracking. A live reconnect requires a still-loaded matching world.
Physics is not paused by network loss: it does not undo a fall/death while
unplugged. Unsupported/incompatible routes, absent checkpoints and disabled
rules use the normal spawn rather than guessing a world coordinate. Root
alignment and real Steam recovery still require in-game testing.

## Build and tests

```bash
dotnet build src/Core/WKMultiPlayerMod.csproj -c Release -p:DeployToGame=false
dotnet test tests/Progress.Tests/Progress.Tests.csproj -c Release
```

On this workstation only .NET 10 is installed, so run the net8 tests with:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/Progress.Tests/Progress.Tests.csproj -c Release
```

The 65 tests cover entry callback/result ordering, resume tickets,
native-handle close matching, spawn policy,
death/rejoin bookkeeping, response correlation/authentication and bounded
snapshot compression. They do not execute Unity or simulate Steam transport.
Builds contain existing nullable/analyzer warnings, but no errors.

To package the built DLLs with the verified upstream bundle, icons, localization
and testing instructions (Python 3; no third-party dependencies):

```bash
python3 scripts/package-preview.py
```

Output: `bin/Release/WKMultiMod-1.8.3.3-preview.zip` and a SHA-256 checksum file.
Both players should extract the ZIP's `MP` folder into `BepInEx/plugins/`,
replacing the existing `MP` files while the game is closed. Keep backups outside
`BepInEx/plugins/` so the old DLLs are not loaded alongside the preview.

## In-game acceptance checklist

Use backups and a disposable run on both computers:

1. Host reaches a checkpoint, moves beyond it; new client joins at the
   checkpoint, **not** at the host's current position. Client keeps only its own
   starting items.
2. Repeat with a checkpoint in a later region that the client has not loaded.
   Verify terrain/collisions and both players' coordinates align.
3. Client reaches an earlier checkpoint and carries items in hands, bag, normal
   pouches, extra pouches and pockets (include an artifact). Die and restart:
   spawn at the later host checkpoint with all those item locations empty.
4. Repeat death followed by `leave`/`join`, menu/rejoin, failed joins, and repeated
   attempts. No pre-death loot or auto-command kit may be restored.
5. Host checkpoint equal/earlier or on another branch: normal respawn, no
   backward/branch teleport.
6. Client alive with loot: interrupt network, restore it, use `reconnect` if
   needed. Keep the live position/inventory; do not invoke checkpoint placement.
7. Leave/cancel during join or checkpoint loading; late responses must not move
   the player into a cancelled session or leave input permanently locked.
8. Check no duplicate pitons/items/enemy replicas after world regeneration or
   reconnect, and verify world pickup/death history is refreshed.
9. Verify ordinary restart, disk/perk revives, Playground, host migration and
   disabled settings. Check BepInEx logs for `[MP Checkpoint]` and `[MP Reconnect]`.
