using HarmonyLib;
using System;
using WKMPMod.Core;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(CL_SaveManager), "Start")]
public static class Patch_NativeContinueStart {
	[HarmonyPrefix]
	public static void Prefix() {
		if (CL_SaveManager.loadSessionSaveOnLoad) NativeRunPersistence.BeginNativeContinue();
	}
}

[HarmonyPatch(typeof(CL_SaveManager), nameof(CL_SaveManager.LoadSession))]
public static class Patch_NativeContinueScheduled {
	[HarmonyPostfix]
	public static void Postfix() => NativeRunPersistence.NativeContinueStarted();

	[HarmonyFinalizer]
	public static Exception? Finalizer(Exception? __exception) {
		if (__exception != null) NativeRunPersistence.NativeContinueStarted();
		return __exception; // Never hide or replace a native save/load error.
	}
}

[HarmonyPatch(typeof(CL_SaveManager), nameof(CL_SaveManager.LoadSave))]
public static class Patch_NativeRunLoaded {
	[HarmonyPrefix]
	public static void Prefix(CL_SaveManager.SaveState __0, CL_SaveManager.SessionSave __1) => NativeRunPersistence.NativeSaveLoaded(__0, __1);
}

[HarmonyPatch(typeof(CL_SaveManager), nameof(CL_SaveManager.SaveSession))]
public static class Patch_NativeRunSaving {
	[HarmonyPrefix]
	public static void Prefix() => NativeRunPersistence.RefreshBeforeSave();

	[HarmonyPostfix]
	public static void Postfix(CL_SaveManager.SessionSave __result) => NativeRunPersistence.RememberNativeSnapshot(__result);
}

[HarmonyPatch(typeof(CL_SaveManager), nameof(CL_SaveManager.SaveSessionToFile))]
public static class Patch_NativeRunFileSaving {
	[HarmonyPrefix]
	public static void Prefix(CL_SaveManager.SessionSave __0) => NativeRunPersistence.BeforeSaveToFile(__0);
}
