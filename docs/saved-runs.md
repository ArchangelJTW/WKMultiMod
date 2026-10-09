# Next-day multiplayer continuation — preview 1.8.3.4

**Implemented/build-tested; still requires a real two-player save/quit/relaunch test.**
Use save backups and a disposable run first. Both players must use this build.

## How to continue

### Host

1. Open the ordinary game-mode screen for your saved run.
2. Choose **Continue Multiplayer**, next to the game's native Continue button.
3. Wait for the saved world to finish loading, then invite your friend to the new lobby.

Fallback: use ordinary **Continue**, wait until your player/world is loaded, and
run `host <name> friends 8` in the console. `host` now attaches multiplayer to the
loaded run. It does **not** call `ReloadWithSeed`, restart the scene, teleport the
host, replace the host's inventory, or start a new run.

Steam creates a **new lobby ID** after the old lobby has closed. Use the new
invite/ID; yesterday's `reconnect` target is not a durable game-session ID.

### Returning guest

Join the new lobby normally. For saves made with this build, the mod looks for
**your own** native save of the same multiplayer run. Matching requires the
persistent random run ID, your Steam ID, the seed, and the complete mode/settings
signature. Same seed alone is deliberately insufficient.

A match opts into the game's native Continue path on your computer. Your native
inventory/perks/lives are loaded locally, then the existing checkpoint-placement
policy aligns you with the host. Your equipment is never read from the host's
save or transferred over the network. An alive next-day continuation normally
keeps your native saved equipment; a saved pending death still uses the empty
inventory rule when advancing to a later actual host checkpoint.

A local save you explicitly continued before joining can also be attached without
another scene load if that same identity matches. Join auto-commands are not
replayed on a matched continuation, to avoid restarting the run or regranting kits.

Each player must let the game write **their own** save using its normal saving/
exit flow. Do not copy the host's save onto the guest's computer. Force-killing the
process cannot restore progress or inventory that the native game never saved.

## Existing saves / first upgrade

Older saves still use native Continue and can be hosted without a restart. They
do not contain a persistent multiplayer run ID, so a returning guest's old save
is **not automatically guessed** to match. Normal late-join rules apply until the
run has been saved on both computers with this build.

When a resumed legacy host has no persisted actual checkpoint snapshot, late
joiners may use the **native saved continuation point** instead. This point is
captured from the native load's saved level/respawn pose, not where the host walks
after loading. It is explicitly **not** a reached checkpoint: it can never
advance a death spawn or erase a dead player's inventory through the checkpoint
rule. Once the host reaches an actual checkpoint, that checkpoint takes priority.

## Implementation and safety

- Calls `UI_GamemodeScreen_Panel.LoadGamemodeSave()` for Continue Multiplayer,
  never the new-game action. Uses the native Continue reference/action rather
  than testing English text or dereferencing a missing menu path.
- Retains native save/no-save visibility. Cancelling/changing the menu while
  Steam creation is pending prevents an old button result from loading a save.
- Waits for native Start/Continue and final world loading before publishing the
  restored mode/seed. Clients can wait up to 60 seconds for the host's mode data,
  instead of abandoning initialization after three seconds during a long load.
- Restores the native `SessionSave.worldSeed` before saved generation begins;
  the inspected game version's Continue path otherwise leaves the loader's
  newly chosen seed in place.
- Stores only versioned mod bookkeeping in the native session flag
  `wkmp.multiplayer-run.v1`: run identity, local checkpoint/death progress,
  geometry-only host checkpoint, and item-removal/enemy-death history.
- Uses the game's normal gzip session file in `Application.persistentDataPath/Sessions/`.
  No separate inventory format, host save-file transfer, or replacement save
  writer is introduced. Native death/clear-item/life consumption stays native.
- Checkpoint geometry is saved relative to an identified native saved level and
  rebound to its loaded transform after Continue. Room generation ID and name
  must match; repeated prefab names are not treated as interchangeable.
- Keeps bounded per-native-snapshot anchors separately because native session
  flags can share references with the live manager. A later flag update must
  not pair an old saved room with the host's current walking room.
- Bounds metadata to 2 MiB of JSON, 20,000 item-removal records and 20,000 enemy
  records. JSON type-name instantiation is disabled. Unsupported/corrupt metadata
  is ignored with a warning; native save/load is not suppressed.
- Saving refreshes metadata even after a manual leave while the same run is still
  loaded. It does not overwrite retained logical history with empty offline tables.
- Captures the client's death tombstone before native Die marks the player dead,
  so leaving/quitting cannot turn a saved death into a pre-death live reconnect.

## Limitations

- This is native-save continuation, not a full authoritative server save or crash
  recovery service. The game remains responsible for what player/world state it
  actually writes. Physics is not rolled back to an unsaved earlier instant.
- There is normally one native save slot per mode/difficulty. Playing/saving a
  different run can replace that slot; the mod will not treat it as the old party.
- No matching guest save means normal late join, not inventory reconstruction.
- Disabling checkpoint join/respawn settings retains their previous behavior.
- Item/enemy history is persisted, but this does not add a new cross-process
  archive of every unloaded network piton/dropped physics object. Check native
  placed-object restoration and possible duplicates in-game before relying on it.
- Unsupported routes, missing generation identities and invalid geometry fall
  back rather than guessing a position. Legacy save lifecycles, side branches,
  rebasing and native death saves particularly need in-game verification.

## Acceptance checklist

1. Start a disposable multiplayer run. Both players collect **different** items.
2. Check `allplayer`: the local player has a real position; the remote player is
   initially `not synced yet`, then has a real position/model after metadata/pose
   exchange. Confirm there is no endless empty-member-data/missing-model loop.
3. Host reaches a checkpoint and moves beyond it. Both use normal save/exit and
   **fully close the game** on both computers.
4. Relaunch. Host chooses Continue Multiplayer. Check the saved area, inventory,
   seed and elapsed/progression state; none should be replaced by a new run.
5. Guest joins via the **new** lobby ID. Verify `[MP Save]` continuation messages,
   the host checkpoint placement and each player's own different saved equipment.
6. Repeat using ordinary Continue followed by `host test friends 8`. The host's
   position/equipment must remain unchanged when issuing the command.
7. Repeat with a pre-1.8.3.4 host save: late join should use its native saved
   continuation point if no real checkpoint is available. Walking after loading
   must not move that anchor, and deaths must not treat it as a later checkpoint.
8. Die before leaving/saving, then relaunch/join. Check native death/life rules;
   advancing to a later actual host checkpoint must not restore pre-death loot.
9. Try another run with the same seed, a different player/Steam ID, changed
   settings, or malformed/future metadata. No unrelated guest inventory may load.
10. Test saved side branches, rebased worlds, native disk revives and placed pitons.
    Retain BOTH players' `BepInEx/LogOutput.log` before restarting after a failure.

Rollback: close the game and restore your backed-up mod folder outside/into the
normal plugin location. Native save schema is unchanged; only a namespaced flag
was added. Use your save backup if you want to undo gameplay/save changes too.
