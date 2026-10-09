# Reconnect, checkpoint and native-save preview — 1.8.3.7

Based on upstream `dev` commit `af7e782555f96ec72e4775df623299079c46d786`.
Uses the unchanged working 1.8.3 resources in `resources/1.8.3/`.

**Implemented and build-tested; not yet validated in the game with two players.**
Both computers should install 1.8.3.7. Checkpoint protocol 2 carries the
persistent run ID and a decision reason; 1.8.3.5 checkpoint hosts are refused
rather than silently falling back to normal spawn.
Never load it alongside another copy
of the multiplayer DLLs. The installed game files are not modified by building.

**Next-day multiplayer continuation:** see [saved-runs.md](saved-runs.md) for
Continue Multiplayer, hosting a loaded save without restarting, returning guests'
own native saves, legacy saves and the save/quit/relaunch checklist.

## Gameplay rules

| Situation | Spawn | Items |
| --- | --- | --- |
| Connection loss while alive; same world still loaded | Keep your own position | Keep your own inventory |
| First-time/late join | Host's latest reached checkpoint, if available on the same route | Keep your own starting items; never copy host equipment |
| Death, with host checkpoint further along the same route | Advance to that host checkpoint | Empty hands, backpack, normal/extra pouches and pockets, including artifacts |
| Death, host checkpoint equal/earlier/unrelated or unavailable | Normal respawn | Normal game rules; no restoration of pre-death inventory |
| Matching persisted multiplayer run after quitting; new lobby ID | Native Continue, then host checkpoint placement | Your own native saved items; native death rules still apply |
| Resumed legacy host without a persisted real checkpoint | Native saved continuation point for late joins only | Your own starting items; never copy host equipment |
| Unrelated run, world seed or mode | Normal initialization | No old reconnect or guest-save state |

The death rule also applies to dying, leaving and rejoining. A death tombstone
is kept across failed joins, scene reloads and returning to the menu within this
game process. Native saves can persist it for the same owned multiplayer run,
but it cannot transfer to an unrelated run/player/seed/mode. A failed
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

- The 1.8.3.6 client log identified a real post-regeneration failure, not a Steam
  kick: `Regenerated checkpoint level identity does not match the host snapshot`
  caused our admission gate to disconnect. The native `GenerateLevels` allocates
  IDs with `WorldLoader.idCounter++`; `saveLoad` does not restore the supplied
  numeric IDs and `UnloadAllLevels` does not reset that counter. A host room at
  ID 0 became guest room ID 50. Checkpoint regeneration now seeds the isolated
  native counter with the host's target ordinal before loading. Name/ID checks
  remain strict; diagnostics report expected and actual identities.
- Fresh in-game retry waits for a NEW scene-loaded revision as well as native
  world readiness. A still-loaded old world cannot send a checkpoint request
  before `SceneManager.LoadScene` completes and then have it cancelled by that
  callback. The death/restart path uses the same barrier; warm reconnect and an
  explicitly loaded matching local native save do not reload unnecessarily.
- Failed placement does not rebind entities or request world-history chunks.
  Initial joins do that only after successful admission; the lobby-entered
  message also waits for admission instead of just a loaded starting map.
- Scopes death/checkpoint state, live reconnect tickets, admission and network
  request/reply to the persistent run ID, not only Steam lobby/seed/mode. The
  paired 1.8.3.5 logs showed a new run ID after a host death/reload in the same
  lobby/seed; old death/branch state and cached old worlds must not carry over.
- Logs the join path and full host/client checkpoint decision, including
  `NoHostCheckpoint`, `DifferentRoutes`, `HostNotLater`, disabled rules and
  snapshot encoding failures. The prior logs showed real interlude captures
  but did not log why later joins were accepted as normal spawn.
- Refuses failed encoding/invalid progress as `Failed`, not `Normal`.
- Clears stale live host geometry on scene generation; restores valid saved
  geometry after binding the new/current run identity. Automatic death retry
  cannot race an unadmitted client's initial checkpoint handshake.
- Ignores unassigned zero-ID item removals, normalizes unknown/empty teams,
  and filters invalid zero IDs when exporting local deletion history. This
  addresses `Invalid saved item history` warnings in the host's 1.8.3.5 log,
  without weakening the saved metadata decoder.
- Uses an explicit public-field Newtonsoft JSON contract for checkpoint
  serialization/cloning/save geometry. In the 1.8.3.4 in-game interlude packet,
  Unity JsonUtility omitted nested `progress`, causing repeated validation
  failures and leaving the client at the beginning. Progress is now required on
  the wire and retained through compression, clone and native-save round trips.
- Loading a starting world is not network admission. Player poses, inventory
  snapshots and world changes remain blocked until checkpoint placement succeeds
  or the host explicitly returns normal spawn. An invalid reply/timeout/failed
  regeneration leaves the join failed rather than syncing two unrelated worlds.
- Checks the regenerated level's generation ID as well as its room name, and logs
  the target room/ID/seed before loading and alignment.
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

A live reconnect requires a still-loaded matching world. Process exit discards
unsaved live state; next-day continuation instead uses the game's native save
and the matching persistent run identity described in [saved-runs.md](saved-runs.md).
This is not crash recovery or a rollback service. Physics is not paused by network
loss: it does not undo a fall/death while unplugged. Unsupported/incompatible routes, absent checkpoints and disabled
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

The 156 tests cover queued scene-load readiness barriers, retained native
counter/host target identity, run-ID changes in the same lobby/seed, stale-run responses
and cached worlds, zero-ID/unknown-team deletion history normalization,
checkpoint decision reasons, JSON/clone/compression field preservation and
failed/successful world admission, entry callback/result ordering, live resume tickets,
native-handle close matching, persistent run/player/seed/mode matching, bounded
save metadata, saved death/continuation policy, spawn policy, death/rejoin
bookkeeping, response correlation/authentication and bounded snapshot compression. They do not execute Unity or simulate Steam transport.
Builds contain existing nullable/analyzer warnings, but no errors.

To package the built DLLs with the verified upstream bundle, icons, localization
and testing instructions (Python 3; no third-party dependencies):

```bash
python3 scripts/package-preview.py
```

Output: `bin/Release/WKMultiMod-1.8.3.7-preview.zip` and a SHA-256 checksum file.
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
   disabled settings. Check logs for `[MP Checkpoint]`, `[MP Reconnect]` and `[MP Save]`.
10. Follow the full save/quit/relaunch checklist in [saved-runs.md](saved-runs.md).
11. Re-test a host resumed in the Silo/Pipeworks interlude. A fresh client must
    load that actual room, not show a nearby ghost through first-area terrain.
    Confirm `[MP Checkpoint] Loading host level ...` and `Joined at ...` appear.
    Invalid snapshot/timeout tests must refuse admission instead of showing
    apparently close players in different rooms.
12. Collect both logs BEFORE using `tpto`: manual teleport is not proof of
    automatic checkpoint placement. In a fresh join, expect `Requesting LateJoin`,
    `Host response: decision=HostWithOwnItems`, `Loading host level ...`, then
    `checkpoint resolution=Applied`. For a normal spawn, inspect the logged reason
    instead of assuming terrain/serialization failed.
13. Restart/replace a host run inside the same Steam lobby with the same seed.
    Old-world syncing must stop; reconnect must not reuse the previous run's
    loaded world, branch checkpoint or death tombstone.
14. Re-test the 1.8.3.6 placement-then-disconnect case. Expect
    `Resetting native generation counter ... -> ...`, `Generated target verified`,
    and `checkpoint resolution=Applied`. Both remote models must stay visible
    and updating. Retry from the game after a failed join: the next checkpoint
    request must follow `Client world ready` at a new scene revision, not run
    from the previous level and get cancelled immediately.
