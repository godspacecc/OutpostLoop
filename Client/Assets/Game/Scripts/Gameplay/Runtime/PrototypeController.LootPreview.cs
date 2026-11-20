#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 编辑器样片在搜取、储物和装备间保持同一列背包位置。
        internal const string LootPreviewPreference = "OutpostLoop.RiverValleyLootPreview";
        internal const string LootPreviewBatch = "LootUiPreview-20261120";
        private const string LootPreviewMenu = "OutpostLoop/预览/切换搜取与背包样片";
        private readonly Dictionary<string, Texture2D> lootPreviewTextures = new();
        private Vector2 lootPackScroll;
        private static readonly Color LootBackground = new(.052f, .078f, .078f, .97f);
        private static readonly Color LootLine = new(.25f, .36f, .34f);
        private static readonly Color LootText = new(.91f, .94f, .91f);
        private static readonly Color LootMuted = new(.65f, .75f, .71f);
        private static readonly Color LootWarning = new(1f, .77f, .44f);
        private bool LootPreviewActive => !survivalBuildOpen &&
            (survivalPackOpen || survivalContainer != null || scavengeRun.OpenCache >= 0) &&
            EditorPrefs.GetBool(LootPreviewPreference);

        private SurvivalContainer LootPreviewContainer => scavengeRun.OpenCache >= 0
            ? scavengeRun.Survival.caches[scavengeRun.OpenCache] : survivalContainer;

        /// <summary>线索只占来源列的空间，装备列预留武器、手电和电池操作。</summary>
        private float LootLeftContentHeight(SurvivalContainer container, bool searching)
        {
            int rows = container == null ? 3 : Mathf.Max(1, container.items.Count);
            return (searching ? 1 : rows) * 39 + (scavengeRun.CanReadWarehouseTransferNote ? 46 : 0);
        }

        /// <summary>限制面板最大高度，短列表只占所需空间，输入区域跟随同一个矩形。</summary>
        private float LootPreviewHeight()
        {
            var state = scavengeRun.Survival;
            float leftHeight = LootLeftContentHeight(LootPreviewContainer, scavengeRun.SearchRemaining > 0);
            return Mathf.Min(395, 92 + Mathf.Max(leftHeight, Mathf.Max(2, state.pack.Count) * 39));
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
            var container = LootPreviewContainer;
            bool searching = scavengeRun.SearchRemaining > 0;
            bool canMoveItems = !searching && state.action == SurvivalAction.None && !state.sleeping;
            float x = panel.x + 16;
            float innerWidth = panel.width - 32;
            float columnWidth = (innerWidth - 14) / 2;
            float right = x + columnWidth + 14;
            float top = panel.y + 77;

            ScavengeFill(panel, LootBackground);
            if (Event.current.type == EventType.Repaint && LootTexture("frame") is Texture2D frame)
                GUI.DrawTexture(panel, frame, ScaleMode.StretchToFill, true, 0, LootLine, new Vector4(7, 7, 7, 7), Vector4.zero);
            ScavengeFill(new Rect(x, panel.y + 49, innerWidth, 1), LootLine);
            string title = opened >= 0 ? scavengeRun.CacheNames[opened] :
                container == null ? "物品与装备" : state.drops.Contains(container) ? "地面物品" : "储物柜";
            LootLabel(new Rect(x, panel.y + 9, innerWidth - 210, 33), title, 21, LootText, true);
            if (container != null && LootButton(new Rect(panel.xMax - 214, panel.y + 11, 130, 29), "使用 / 装备", !searching))
            {
                OpenSurvivalPack();
                return true;
            }
            if (LootButton(new Rect(panel.xMax - 76, panel.y + 11, 60, 29), "关闭 E", true))
            {
                CloseSurvivalPanels();
                return true;
            }
            LootLabel(new Rect(x, panel.y + 51, columnWidth, 25), container == null ? "已装备" : "来源物资", 13, LootMuted, true);
            LootLabel(new Rect(right, panel.y + 51, 85, 25), "随身背包", 13, LootMuted, true);
            DrawLootCapacity(right + 94, panel.y + 51, state.Used);
            float viewportHeight = panel.yMax - top - 12;
            ScavengeFill(new Rect(right - 8, top, 1, viewportHeight), LootLine);
            float cellWidth = columnWidth - 9;
            var leftViewport = new Rect(x, top, columnWidth + 6, viewportHeight);
            survivalScroll = GUI.BeginScrollView(leftViewport, survivalScroll,
                new Rect(0, 0, cellWidth, Mathf.Max(LootLeftContentHeight(container, searching), viewportHeight - 1)));
            try
            {
                if (container == null) DrawLootEquipment(cellWidth, canMoveItems);
                else
                {
                    float leftY = 0;
                    if (scavengeRun.CanReadWarehouseTransferNote)
                    {
                        LootLabel(new Rect(8, 0, cellWidth - 8, 20), "药品转运记录", 13, LootWarning);
                        LootLabel(new Rect(8, 20, cellWidth - 8, 20), "急救用品已转运至区域医院一楼。", 13, LootText);
                        leftY = 46;
                    }
                    if (searching || container.items.Count == 0)
                        LootLabel(new Rect(8, leftY, cellWidth - 8, 38),
                            searching ? $"搜索中 · {scavengeRun.SearchRemaining:0.0} 秒" : "空", 14, LootMuted);
                    if (!searching)
                        DrawLootRows(container.items, state.pack, 0, leftY, cellWidth, ScavengeSurvival.Capacity, true, canMoveItems);
                }
            }
            finally { GUI.EndScrollView(); }

            var packViewport = new Rect(right - 8, top, columnWidth + 6, viewportHeight);
            lootPackScroll = GUI.BeginScrollView(packViewport, lootPackScroll,
                new Rect(0, 0, cellWidth, Mathf.Max(state.pack.Count * 39, viewportHeight - 1)));
            try
            {
                if (container == null) DrawLootPackActions(0, cellWidth, canMoveItems);
                else DrawLootRows(state.pack, container.items, 0, 0, cellWidth, 12, false, canMoveItems);
                if (state.pack.Count == 0)
                    LootLabel(new Rect(8, 0, cellWidth - 8, 38), "背包为空", 14, LootMuted);
            }
            finally { GUI.EndScrollView(); }
            return true;
        }

        /// <summary>用逐格填充表示真实占用，颜色和数字同时提示满包。</summary>
        private static void DrawLootCapacity(float x, float y, int used)
        {
            const int capacity = ScavengeSurvival.Capacity;
            Color fill = used >= capacity ? LootWarning : new Color(.52f, .76f, .64f);
            for (int slot = 0; slot < capacity; slot++)
            {
                var cell = new Rect(x + slot * 16, y + 7, 13, 11);
                ScavengeFill(cell, LootLine);
                ScavengeFill(new Rect(cell.x + 1, cell.y + 1, 11, 9), slot < used ? fill : LootBackground);
            }
            LootLabel(new Rect(x + capacity * 16 + 6, y, 52, 25), $"{used}/{capacity}", 13, fill, true);
        }

        /// <summary>以图标和原物品名称展示每一组物品，沿用单次拾取与放入规则。</summary>
        private void DrawLootRows(List<SurvivalStack> source, List<SurvivalStack> target, float x, float y,
            float width, int capacity, bool take, bool enabled)
        {
            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                var row = new Rect(x, y + i * 39, width, 35);
                bool available = enabled && ScavengeSurvival.CanReceive(item, target, capacity, out _);
                DrawLootItem(row, item, !take, available);
                float buttonX = take ? row.xMax - 68 : row.x + 4;
                if (!LootButton(new Rect(buttonX, row.y + 4, 64, 27), take ? "拾取 →" : "← 放入", available))
                    continue;
                if (!ScavengeSurvival.Transfer(source, i, target, capacity))
                    scavengeRun.ReportSurvival(take ? "背包已满" : "容器已满");
                return;
            }
        }

        /// <summary>右列物品图标不随动作切换移动。</summary>
        private void DrawLootItem(Rect row, SurvivalStack item, bool inPack, bool enabled)
        {
            bool hover = enabled && row.Contains(Event.current.mousePosition);
            ScavengeFill(row, hover ? new Color(.16f, .24f, .22f) : new Color(.092f, .137f, .132f));
            float iconX = row.x + (inPack ? 72 : 0);
            ScavengeFill(new Rect(iconX, row.y, 34, 35), new Color(.14f, .20f, .19f));
            if (Event.current.type == EventType.Repaint && LootTexture(item.item.ToString()) is Texture2D icon)
                GUI.DrawTexture(new Rect(iconX + 3, row.y + 3, 28, 28), icon, ScaleMode.ScaleToFit);
            if (item.item is SurvivalItem.Crowbar or SurvivalItem.Pistol)
                LootLabel(new Rect(iconX + 4, row.y, 29, 35), item.item == SurvivalItem.Crowbar ? "工具" : "武器", 11, LootMuted);
            LootLabel(new Rect(iconX + 41, row.y, row.width - 117, 35), DescribeSurvivalItem(item), 14, LootText);
        }

        /// <summary>在固定的右列操作使用、装备和放下。</summary>
        private void DrawLootPackActions(float x, float width, bool enabled)
        {
            var pack = scavengeRun.Survival.pack;
            for (int index = 0; index < pack.Count; index++)
            {
                var item = pack[index];
                var row = new Rect(x, index * 39, width, 35);
                DrawLootItem(row, item, true, enabled);
                var action = ActionForSurvivalItem(item.item);
                bool equip = IsSurvivalEquipment(item.item);
                if ((equip || action != SurvivalAction.None) &&
                    LootButton(new Rect(x + 3, row.y + 4, 32, 27), equip ? "装备" : "使用", enabled))
                {
                    UseSurvivalPackItem(index, equip, action);
                    return;
                }
                if (LootButton(new Rect(x + 37, row.y + 4, 32, 27), "放下", enabled))
                {
                    DropSurvivalPackItem(index);
                    return;
                }
            }
        }

        /// <summary>左列显示当前装备与对应操作。</summary>
        private void DrawLootEquipment(float width, bool enabled)
        {
            var state = scavengeRun.Survival;
            DrawLootEquipmentSlot(state.weapon, false, 0, width, enabled);
            DrawLootEquipmentSlot(state.torch, true, 39, width, enabled);
            if (state.torch != null && LootButton(new Rect(3, 82, 112, 27), "更换手电电池", enabled))
                StartSurvivalAction(SurvivalAction.Battery);
        }

        private void DrawLootEquipmentSlot(SurvivalStack item, bool torch, float y, float width, bool enabled)
        {
            if (item == null)
            {
                LootLabel(new Rect(8, y, width - 8, 35), torch ? "手电 · 空" : "武器 · 空", 14, LootMuted);
                return;
            }
            DrawLootItem(new Rect(0, y, width, 35), item, false, enabled);
            bool hasSpace = scavengeRun.Survival.Used + item.Slots <= ScavengeSurvival.Capacity;
            if (LootButton(new Rect(width - 68, y + 4, 64, 27), "卸下 →", enabled && hasSpace) &&
                !scavengeRun.Survival.Stow(torch))
                scavengeRun.ReportSurvival("背包没有空格");
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
            var style = new GUIStyle(GUI.skin.button) { fontSize = 13, padding = new RectOffset(2, 2, 1, 1) };
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
            GUI.backgroundColor = !GUI.enabled ? new Color(.16f, .18f, .17f) :
                rect.Contains(Event.current.mousePosition)
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
