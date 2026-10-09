# Upstream 1.8.3 resources

`1.8.3/` is an unmodified snapshot of the author's complete Thunderstore package.
It contains the matching `player_prefab` asset bundle, grab/drag icons, artwork
(including `Sprite.psd`), localization files, package metadata, and upstream
compiled DLLs for reference. The original ZIP is also retained locally.

## Provenance

- Package: `WK_Simple_Multi-Simple_Multi_Player-1.8.3`
- Download: https://thunderstore.io/package/download/WK_Simple_Multi/Simple_Multi_Player/1.8.3/
- Package page: https://thunderstore.io/c/white-knuckle/p/WK_Simple_Multi/Simple_Multi_Player/
- Source: https://github.com/Shen-X-L/WKMultiMod/tree/dev
- Source checkout when acquired: `af7e782555f96ec72e4775df623299079c46d786`
- Bundle SHA-256: `30527fffcc7a5e7219c53779ae80ea4be6a96f207e6ddd4648c4f07419723f3d`

The package's ZIP CRCs and extracted contents were verified. `1.8.3/SHA256SUMS`
records every upstream file plus the original archive. Verify with:

```bash
cd resources/1.8.3
sha256sum --check SHA256SUMS
```

## Using these resources

For future 1.8.3-based builds, reuse `player_prefab`, `grub.png`, and `hang.png`
alongside freshly built `WKMultiPlayerMod.dll` and `WKMultiPlayerMod.Shared.dll`.
Use the localization files in `src/Core/Util/Localization/` when making code or
text changes; the copies here remain an upstream reference snapshot.

Do not mix the 1.8.3 bundle with 1.8.1 DLLs. Keep upstream reference DLLs out of
your game installation when deploying a custom build; there must be only one
installed copy of the mod. ZIPs and compiled reference DLLs are git-ignored.

This is a compiled Unity asset bundle, not the original Unity editor project.
Most C# gameplay/network/UI changes can reuse it. Changes to serialized
components or prefab structure may require a matching updated bundle.

No game/BepInEx DLLs, personal configuration, or game saves are included here.
