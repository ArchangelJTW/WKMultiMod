using HarmonyLib;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WKMPMod.Core;
using WKMPMod.Util;
using Object = UnityEngine.Object;

namespace WKMPMod.UI;

// 游戏模式菜单初始化时添加Multi play按钮
[HarmonyPatch(typeof(UI_GamemodeScreen), nameof(UI_GamemodeScreen.Initialize))]
public class Patch_UI_GamemodeScreen_Initialize {
	static void Postfix(UI_GamemodeScreen __instance, M_Gamemode mode) {
		// 获取刚刚显示出来的面板实例
		string panelId = mode.gamemodePanel.id;
		if (!__instance.activePanels.TryGetValue(panelId, out var panel)) return;

		// Use native references/actions, not English button labels. Saved runs have
		// a distinct Continue action; cloning the new-game button cannot resume them.
		var newGame = panel.transform.Find("Pages/Gamemode_NewGame_Screen/Tab Selection Hor/Play")?.GetComponent<Button>()
			?? panel.playText?.GetComponentInParent<Button>();
		var continueGame = panel.continueButtonText?.GetComponentInParent<Button>()
			?? panel.GetComponentsInChildren<Button>(true).FirstOrDefault(CallsNativeContinue);
		AddHostButton(panel, newGame, false);
		AddHostButton(panel, continueGame, true);
		// Apply native save visibility once more now that the extra buttons exist.
		__instance.RefreshCurrentGamemode();
	}

	private static bool CallsNativeContinue(Button button) {
		for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
			if (button.onClick.GetPersistentMethodName(i) == nameof(UI_GamemodeScreen_Panel.LoadGamemodeSave)) return true;
		return false;
	}

	private static void AddHostButton(UI_GamemodeScreen_Panel panel, Button? template, bool resume) {
		if (template == null || panel.GetComponentsInChildren<UI_LobbyCreateButton>(true).Any(b => b.resumeSavedRun == resume)) return;
		var clone = Object.Instantiate(template.gameObject, template.transform.parent);
		clone.name = resume ? "Continue Multiplayer" : "Multi Play";
		clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
		var create = clone.AddComponent<UI_LobbyCreateButton>();
		create.gamemodePanel = panel;
		create.resumeSavedRun = resume;
		var text = clone.GetComponentInChildren<TMP_Text>(true);
		if (text != null) text.text = Localization.GetSmart(resume
			? "UI_Manager.GamemodeScreen Continue Multiplayer" : "UI_Manager.GamemodeScreen Multi Play");
		// Keep native save/no-save visibility semantics, and immediately mirror the
		// template since Initialize may already have refreshed the native lists.
		if (resume) panel.hasSaveObjects.Add(clone);
		else panel.noSaveObjects.Add(clone);
		clone.SetActive(template.gameObject.activeSelf);
	}
}

// 修复Facility Button初始化导致Facility界面返回绑定引用错误的问题
[HarmonyPatch(typeof(UI_MenuButton), nameof(UI_MenuButton.Initialize))]
public class Patch_UI_MenuButton_Initialize {
	static bool Prefix(UI_MenuButton __instance, UI_Menu menu) {
		// 检查这个按钮是否属于我们克隆出来的菜单
		if (UI_Manager.IsCloningMultiplayerMenu && __instance.gameObject.name == "Facility Button") {
			return false;
		}
		return true; // 其他按钮正常执行
	}
}