using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    public sealed partial class RimUnityBuildWindow
    {
        private GUIStyle inventoryButton, inventoryPopup, inventorySearch, inventoryTabs, inventoryCaption, inventoryHeading;
        private GUIStyle settingsInput, settingsToggle, logText;

        private void InventoryStyles()
        {
            if (inventoryButton != null) return;
            inventoryButton = new GUIStyle(EditorStyles.miniButton) { fontSize = 12, fixedHeight = 0, padding = new RectOffset(10, 10, 5, 5) };
            inventoryPopup = new GUIStyle(EditorStyles.popup) { fontSize = 12, fixedHeight = 0 };
            inventorySearch = new GUIStyle(EditorStyles.toolbarSearchField) { fontSize = 12, fixedHeight = 0 };
            inventoryTabs = new GUIStyle(EditorStyles.miniButton) { fontSize = 13, fixedHeight = 0, padding = new RectOffset(12, 12, 6, 6) };
            inventoryCaption = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            inventoryHeading = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            settingsInput = new GUIStyle(EditorStyles.textField) { fontSize = 12, fixedHeight = 0, padding = new RectOffset(8, 8, 6, 6) };
            settingsToggle = new GUIStyle(EditorStyles.toggle) { fontSize = 12, fixedHeight = 0, alignment = TextAnchor.MiddleLeft };
            logText = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true, richText = false, padding = new RectOffset(10, 10, 8, 8) };
            rowText = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset(6, 4, 0, 0) };
            faintText = new GUIStyle(EditorStyles.label) { fontSize = 12, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
            faintText.normal.textColor = EditorGUIUtility.isProSkin ? new Color(.65f, .68f, .72f) : new Color(.38f, .40f, .44f);
        }

        private void DrawBuildHeader()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, 78), PanelColor());
            GUI.Label(new Rect(12, 7, Mathf.Max(100, position.width - 230), 30), "RimUnity  /  明文资源构建管理器", inventoryHeading);
            if (GUI.Button(new Rect(position.width - 214, 8, 96, 30), "百科全书", inventoryButton)) RimUnityEncyclopediaWindow.Open();
            if (GUI.Button(new Rect(position.width - 108, 8, 96, 30), "刷新清点", inventoryButton)) RefreshEntries(true);
            tab = GUI.Toolbar(new Rect(12, 43, Mathf.Min(450, position.width - 24), 30), tab, new[] { "资源清点", "构建设置", "导出日志" }, inventoryTabs);
            EditorGUI.DrawRect(new Rect(0, 77, position.width, 1), DividerColor());
        }

        private bool PrimaryButton(Rect rect, string label)
        {
            Color previous = GUI.backgroundColor;
            try { GUI.backgroundColor = EditorGUIUtility.isProSkin ? new Color(.32f, .60f, .92f) : new Color(.48f, .70f, .97f); return GUI.Button(rect, label, inventoryButton); }
            finally { GUI.backgroundColor = previous; }
        }

        private static Color PanelColor() => EditorGUIUtility.isProSkin ? new Color(.19f, .20f, .21f) : new Color(.85f, .86f, .88f);
        private static Color DividerColor() => EditorGUIUtility.isProSkin ? new Color(.12f, .13f, .14f) : new Color(.66f, .68f, .71f);
        private static Rect InventoryRect(InventoryGeometry.Box box) => new Rect(box.x, box.y, box.width, box.height);
    }
}
