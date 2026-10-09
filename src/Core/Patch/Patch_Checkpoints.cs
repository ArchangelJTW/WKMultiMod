using HarmonyLib;
using System;
using WKMPMod.Core;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(UT_TravelDistanceCheckpoint), nameof(UT_TravelDistanceCheckpoint.SetCheckpoint))]
public static class Patch_ProgressCheckpoint {
	[HarmonyPostfix]
	public static void Postfix() => CheckpointSyncManager.Current?.CaptureCheckpoint();
}

[HarmonyPatch(typeof(WorldLoader), "SetOffset")]
public static class Patch_CheckpointWorldOffset {
	[HarmonyPostfix]
	public static void Postfix(float __0) => CheckpointSyncManager.Current?.OffsetHostCheckpoint(__0);
}

[HarmonyPatch(typeof(CL_SaveManager), nameof(CL_SaveManager.CreateOrUpdateSaveState))]
public static class Patch_RespawnCheckpoint {
	[HarmonyPostfix]
	public static void Postfix(CL_SaveManager.SaveState __result, bool saveToSaveStates) {
		if (!saveToSaveStates || __result == null || __result.type == CL_SaveManager.SaveState.SaveType.session
			|| (__result.flag?.StartsWith("session", StringComparison.OrdinalIgnoreCase) ?? false)) return;
		CheckpointSyncManager.Current?.CaptureCheckpoint(__result);
	}
}
