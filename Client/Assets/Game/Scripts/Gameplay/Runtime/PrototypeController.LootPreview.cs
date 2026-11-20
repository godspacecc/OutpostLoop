#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 独立的编辑器视觉开关。仅实际搜点使用样片，容器和单独背包仍走原入口。
        internal const string LootPreviewPreference = "OutpostLoop.RiverValleyLootPreview";
        internal const string LootPreviewBatch = "LootUiPreview-20261120";
        private const string LootPreviewMenu = "OutpostLoop/预览/切换搜取与背包样片";
        private readonly Dictionary<string, Texture2D> lootPreviewTextures = new();
        private static readonly Color LootBackground = new(.052f, .078f, .078f, .97f);
        private static readonly Color LootLine = new(.25f, .36f, .34f);
        private static readonly Color LootText = new(.91f, .94f, .91f);
        private static readonly Color LootMuted = new(.65f, .75f, .71f);
        private static readonly Color LootWarning = new(1f, .77f, .44f);
        private bool LootPreviewActive => !survivalBuildOpen && scavengeRun.OpenCache >= 0 &&
            EditorPrefs.GetBool(LootPreviewPreference);

        /// <summary>限制面板最大高度，短列表只占所需空间，输入区域跟随同一个矩形。</summary>
        private float LootPreviewHeight()
        {
            var state = scavengeRun.Survival;
            int visibleRows = scavengeRun.SearchRemaining > 0 ? 1 : state.caches[scavengeRun.OpenCache].items.Count;
            int rows = Mathf.Max(2, Mathf.Max(visibleRows, state.pack.Count));
            return Mathf.Min(395, 124 + rows * 39 + (scavengeRun.CanReadWarehouseTransferNote ? 28 : 0));
        }

        /// <summary>切换编辑器样片，记住当前机器的评审选择。</summary>
        [MenuItem(LootPreviewMenu)]
        private static void ToggleLootPreview() =>
            EditorPrefs.SetBool(LootPreviewPreference, !EditorPrefs.GetBool(LootPreviewPreference));

        [MenuItem(LootPreviewMenu, true)]
        private static bool ValidateLootPreviewMenu()
        {
            Menu.SetChecked(LootPreviewMenu, EditorPrefs.GetBool(LootPreviewPreference));
            return true;
        }

        /// <summary>只读取当前搜点与背包，由原有领域方法执行物品转移。</summary>
        private bool TryDrawLootPreview(Rect panel)
        {
            int opened = scavengeRun.OpenCache;
            if (!LootPreviewActive) return false;

            var state = scavengeRun.Survival;
            var container = state.caches[opened];
            bool searching = scavengeRun.SearchRemaining > 0;
            bool full = state.Used >= ScavengeSurvival.Capacity;
            bool canMoveItems = !searching && state.action == SurvivalAction.None && !state.sleeping;
            float x = panel.x + 16;
            float innerWidth = panel.width - 32;
            float columnWidth = (innerWidth - 14) / 2;
            float right = x + columnWidth + 14;
            float top = panel.y + 77;
            float footer = panel.yMax - 42;

            ScavengeFill(panel, LootBackground);
            if (Event.current.type == EventType.Repaint && LootTexture("frame") is Texture2D frame)
                GUI.DrawTexture(panel, frame, ScaleMode.StretchToFill, true, 0, LootLine, new Vector4(7, 7, 7, 7), Vector4.zero);
            ScavengeFill(new Rect(x, panel.y + 49, innerWidth, 1), LootLine);
            LootLabel(new Rect(x, panel.y + 9, innerWidth - 210, 33), scavengeRun.CacheNames[opened], 21, LootText, true);
            if (LootButton(new Rect(panel.xMax - 202, panel.y + 11, 132, 29), "使用 / 装备", !searching))
            {
                CloseSurvivalPanels();
                survivalPackOpen = true;
                return true;
            }
            if (LootButton(new Rect(panel.xMax - 64, panel.y + 11, 48, 29), "关闭", true))
            {
                CloseSurvivalPanels();
                return true;
            }
            LootLabel(new Rect(x, panel.y + 51, columnWidth, 25), "箱内物资", 13, LootMuted, true);
            LootLabel(new Rect(right, panel.y + 51, columnWidth, 25),
                $"随身背包   {state.Used}/{ScavengeSurvival.Capacity}", 13, LootMuted, true);
            if (scavengeRun.CanReadWarehouseTransferNote)
            {
                LootLabel(new Rect(x, top, innerWidth, 26), "药品转运记录 · 急救用品已转运至区域医院一楼。", 13, LootWarning);
                top += 28;
            }
            var viewport = new Rect(x, top, innerWidth, footer - top - 4);
            int rows = Mathf.Max(searching ? 1 : container.items.Count, state.pack.Count);
            survivalScroll = GUI.BeginScrollView(viewport, survivalScroll,
                new Rect(0, 0, innerWidth - 17, Mathf.Max(rows * 39, viewport.height - 1)));
            try
            {
                float cellWidth = columnWidth - 9;
                if (searching || container.items.Count == 0)
                    LootLabel(new Rect(8, 0, cellWidth - 8, 38),
                        searching ? $"搜索中 · {scavengeRun.SearchRemaining:0.0} 秒" : "箱内已空", 14, LootMuted);
                if (!searching)
                    DrawLootRows(container.items, state.pack, 0, cellWidth, ScavengeSurvival.Capacity, true, canMoveItems);
                DrawLootRows(state.pack, container.items, right - x - 8, cellWidth, 12, false, canMoveItems);
                if (state.pack.Count == 0)
                    LootLabel(new Rect(right - x, 0, cellWidth - 8, 38), "背包为空", 14, LootMuted);
            }
            finally { GUI.EndScrollView(); }

            ScavengeFill(new Rect(x, footer, innerWidth, 1), LootLine);
            LootLabel(new Rect(x, footer + 1, innerWidth, 31),
                full ? "背包已满 · 可先放入箱内，或拾取可合并的物品" : "搜取期间，行动仍在继续",
                13, full ? LootWarning : LootMuted);
            return true;
        }

        /// <summary>以图标和原物品名称展示每一组物品，沿用单次拾取与放入规则。</summary>
        private void DrawLootRows(List<SurvivalStack> source, List<SurvivalStack> target, float x,
            float width, int capacity, bool take, bool enabled)
        {
            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                var row = new Rect(x, i * 39, width, 35);
                bool hover = row.Contains(Event.current.mousePosition);
                ScavengeFill(row, hover ? new Color(.16f, .24f, .22f) : new Color(.092f, .137f, .132f));
                ScavengeFill(new Rect(row.x, row.y, 34, 35), new Color(.14f, .20f, .19f));
                if (Event.current.type == EventType.Repaint && LootTexture(item.item.ToString()) is Texture2D icon)
                    GUI.DrawTexture(new Rect(row.x + 3, row.y + 3, 28, 28), icon, ScaleMode.ScaleToFit);
                if (item.item is SurvivalItem.Crowbar or SurvivalItem.Pistol)
                    LootLabel(new Rect(row.x + 4, row.y, 29, 35), item.item == SurvivalItem.Crowbar ? "工具" : "武器", 11, LootMuted);
                LootLabel(new Rect(row.x + 41, row.y, width - 106, 35), DescribeSurvivalItem(item), 14, LootText);
                if (!LootButton(new Rect(row.xMax - 59, row.y + 4, 55, 27), take ? "拾取" : "放入", enabled))
                    continue;
                if (!ScavengeSurvival.Transfer(source, i, target, capacity))
                    scavengeRun.ReportSurvival(take ? "背包已满" : "容器已满");
                return;
            }
        }

        /// <summary>样片文字复用项目当前 IMGUI 字体，颜色和字号仅作用于当前控件。</summary>
        private static void LootLabel(Rect rect, string label, int size, Color color, bool bold = false)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleLeft };
            style.normal.textColor = color;
            if (bold) style.fontStyle = FontStyle.Bold;
            GUI.Label(rect, label, style);
        }

        /// <summary>使用素材包按钮底图和 Unity 原生点击状态，保留禁用保护。</summary>
        private bool LootButton(Rect rect, string label, bool enabled)
        {
            var style = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            if (LootTexture("button") is Texture2D background)
            {
                style.normal.background = background;
                style.hover.background = background;
                style.active.background = background;
                style.border = new RectOffset(8, 8, 8, 8);
            }
            style.normal.textColor = LootText;
            style.hover.textColor = Color.white;
            style.active.textColor = LootWarning;
            bool previous = GUI.enabled;
            Color previousColor = GUI.backgroundColor;
            GUI.enabled = previous && enabled;
            GUI.backgroundColor = Event.current != null && rect.Contains(Event.current.mousePosition)
                ? new Color(.42f, .58f, .48f) : new Color(.27f, .38f, .34f);
            try { return GUI.Button(rect, label, style); }
            finally
            {
                GUI.enabled = previous;
                GUI.backgroundColor = previousColor;
            }
        }

        /// <summary>从本地样片目录按需读取素材，贴图不会进入游戏资源收集。</summary>
        private Texture2D LootTexture(string name)
        {
            if (lootPreviewTextures.TryGetValue(name, out var texture)) return texture;
            string folder = System.Environment.GetEnvironmentVariable("OUTPOST_LOOT_UI_ASSETS");
            if (string.IsNullOrEmpty(folder))
                folder = Path.Combine(Application.dataPath, "../../Backup/Artifacts", LootPreviewBatch, "Sprites");
            var path = Path.Combine(folder, name + ".png");
            if (File.Exists(path))
            {
                texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                if (!texture.LoadImage(File.ReadAllBytes(path), true))
                {
                    Destroy(texture);
                    texture = null;
                }
            }
            lootPreviewTextures.Add(name, texture);
            return texture;
        }

        /// <summary>销毁样片独立加载的贴图。</summary>
        private void ReleaseLootPreviewTextures()
        {
            foreach (var texture in lootPreviewTextures.Values)
                Release(texture);
            lootPreviewTextures.Clear();
        }
    }
}
#endif
