using System;
using System.Collections.Generic;
using System.Text;
using Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 物品页只读领域状态，按钮经原有规则执行。
        private const string InventoryTexturePath = "RiverValleyInventory/";
        private readonly Dictionary<string, Texture2D> lootTextures = new();
        private readonly StringBuilder inventorySnapshot = new();
        private GameObject inventoryRoot;
        private RectTransform inventoryPanel;
        private ScrollRect inventorySourceScroll, inventoryPackScroll;
        private TMP_FontAsset inventoryFont;
        private TMP_Text inventorySearchText;
        private string inventoryRevision;
        private SurvivalContainer inventoryDisplayedSource;
        private Rect inventoryDisplayedBounds;
        private static readonly Color LootBackground = new(.052f, .078f, .078f, .97f);
        private static readonly Color LootLine = new(.25f, .36f, .34f);
        private static readonly Color LootText = new(.91f, .94f, .91f);
        private static readonly Color LootMuted = new(.65f, .75f, .71f);
        private static readonly Color LootWarning = new(1f, .77f, .44f);

        private bool SurvivalInventoryOpen => !survivalBuildOpen &&
            (survivalPackOpen || survivalContainer != null || scavengeRun.OpenCache >= 0);

        private SurvivalContainer CurrentInventorySource => scavengeRun.OpenCache >= 0
            ? scavengeRun.Survival.caches[scavengeRun.OpenCache] : survivalContainer;

        /// <summary>面板与已有世界点击拦截使用同一套 1280×720 坐标。</summary>
        private float SurvivalInventoryHeight()
        {
            var state = scavengeRun.Survival;
            return Mathf.Min(395, 92 + Mathf.Max(InventorySourceHeight(CurrentInventorySource), Mathf.Max(2, state.pack.Count) * 39));
        }

        private float InventorySourceHeight(SurvivalContainer source)
        {
            int rows = source == null ? (scavengeRun.Survival.torch == null ? 2 : 3) : Mathf.Max(1, source.items.Count);
            return (scavengeRun.SearchRemaining > 0 ? 1 : rows) * 39 + (scavengeRun.CanReadWarehouseTransferNote ? 46 : 0);
        }

        private bool CanShowInventoryCanvas() => initialized && !crossing && survivalMode && scavengeRoot != null &&
            scavengeRoot.gameObject.activeSelf && scavengeRun != null && SurvivalInventoryOpen &&
            scavengeRun.Outcome == ScavengeOutcome.Exploring && !scavengePaused && !survivalHelpOpen &&
            !survivalConfirmExit && !survivalRegionMapOpen && !RiverValleyPortalPlaying && !UtilityWindowOpen;

        /// <summary>局内面板跟随实际开关和模态窗口，只在内容或语言有变化时重排。</summary>
        private void LateUpdate()
        {
            if (!CanShowInventoryCanvas())
            {
                if (inventoryRoot && inventoryRoot.activeSelf) inventoryRoot.SetActive(false);
                inventoryRevision = null;
                return;
            }
            if (!inventoryRoot)
            {
                inventoryFont = UnityEngine.Resources.Load<TMP_FontAsset>("RiverValleyInventoryFont");
                inventoryRoot = new GameObject("RiverValleyInventoryCanvas", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                inventoryRoot.transform.SetParent(transform, false);
                var canvas = inventoryRoot.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30;
                var scaler = inventoryRoot.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
            inventoryRoot.SetActive(true);
            var bounds = SurvivalInventory(Screen.width / HudScale, Screen.height / HudScale);
            var source = CurrentInventorySource;
            string revision = InventoryRevision(source);
            if (revision != inventoryRevision || inventoryDisplayedSource != source || inventoryDisplayedBounds != bounds)
            {
                bool preserveScroll = inventoryRevision != null && inventoryDisplayedSource == source;
                float left = preserveScroll ? inventorySourceScroll.verticalNormalizedPosition : 1;
                float right = preserveScroll ? inventoryPackScroll.verticalNormalizedPosition : 1;
                if (inventoryPanel)
                {
                    inventoryPanel.gameObject.SetActive(false);
                    Destroy(inventoryPanel.gameObject);
                }
                BuildInventory(bounds, source);
                Canvas.ForceUpdateCanvases();
                inventorySourceScroll.verticalNormalizedPosition = left;
                inventoryPackScroll.verticalNormalizedPosition = right;
                inventoryRevision = revision;
                inventoryDisplayedSource = source;
                inventoryDisplayedBounds = bounds;
            }
            if (inventorySearchText)
                inventorySearchText.text = Loc.T("InventorySearching", scavengeRun.SearchRemaining);
        }

        /// <summary>签名只含影响 UI 的字段；搜索计时独立刷新，避免重建滚动列表。</summary>
        private string InventoryRevision(SurvivalContainer source)
        {
            var state = scavengeRun.Survival;
            inventorySnapshot.Clear();
            inventorySnapshot.Append(Game.Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value)
                .Append('|').Append(scavengeRun.OpenCache).Append('|').Append(scavengeRun.SearchRemaining > 0)
                .Append('|').Append(scavengeRun.CanReadWarehouseTransferNote).Append('|').Append((int)state.action)
                .Append('|').Append(state.sleeping);
            void Stack(SurvivalStack item)
            {
                inventorySnapshot.Append('|');
                if (item != null)
                    inventorySnapshot.Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item)).Append(',')
                        .Append((int)item.item).Append(',').Append(item.count).Append(',')
                        .Append(item.rounds).Append(',').Append(Mathf.RoundToInt(item.charge / 3.6f));
            }
            Stack(state.weapon);
            Stack(state.torch);
            foreach (var item in state.pack) Stack(item);
            inventorySnapshot.Append('/');
            if (source != null)
                foreach (var item in source.items) Stack(item);
            return inventorySnapshot.ToString();
        }

        /// <summary>左列显示当前来源或装备，右列保留背包位置与容量。</summary>
        private void BuildInventory(Rect bounds, SurvivalContainer source)
        {
            var state = scavengeRun.Survival;
            bool searching = scavengeRun.SearchRemaining > 0;
            bool enabled = InventoryCanMove(source);
            int opened = scavengeRun.OpenCache;
            float innerWidth = bounds.width - 32;
            float columnWidth = (innerWidth - 14) / 2;
            float right = 30 + columnWidth;
            float height = bounds.height - 89;
            float width = columnWidth - 9;
            inventoryPanel = InventoryRect(inventoryRoot.transform, "InventoryPanel", bounds);
            InventoryGraphic(inventoryPanel, "Surface", new Rect(0, 0, bounds.width, bounds.height), LootBackground, hit: true);
            var frame = LootTexture("frame");
            if (frame) InventoryGraphic(inventoryPanel, "Frame", new Rect(0, 0, bounds.width, bounds.height), LootLine, frame);
            InventoryGraphic(inventoryPanel, "HeaderRule", new Rect(16, 49, innerWidth, 1), LootLine);
            string title = Loc.T(source == null ? "InventoryEquipment" : state.drops.Contains(source) ? "InventoryGround" : "InventoryStorage");
            if (opened >= 0)
            {
                string name = scavengeRun.CacheNames[opened];
                string key = "InventorySource." + name;
                title = Loc.T(key);
                if (title == key) title = name;
            }
            InventoryLabel(inventoryPanel, "InventoryTitle", new Rect(16, 8, innerWidth - 210, 35), title, 21, LootText, bold: true);
            if (source != null)
                InventoryButton(inventoryPanel, "UseEquipment", new Rect(bounds.width - 214, 11, 130, 29),
                    Loc.T("InventoryUseEquipment"), !searching, () => { if (CanShowInventoryCanvas()) OpenSurvivalPack(); });
            InventoryButton(inventoryPanel, "CloseInventory", new Rect(bounds.width - 76, 11, 60, 29),
                Loc.T("InventoryClose"), true, CloseSurvivalPanels);
            InventoryLabel(inventoryPanel, "SourceHeading", new Rect(16, 51, columnWidth, 25),
                Loc.T(source == null ? "InventoryEquipped" : "InventoryLoot"), 13, LootMuted, bold: true);
            InventoryLabel(inventoryPanel, "PackHeading", new Rect(right, 51, 90, 25),
                Loc.T("InventoryPack"), 13, LootMuted, bold: true);
            DrawInventoryCapacity(right + 94, state.Used);
            InventoryGraphic(inventoryPanel, "ColumnRule", new Rect(right - 8, 77, 1, height), LootLine);
            inventorySourceScroll = InventoryColumn(inventoryPanel, "InventorySource", new Rect(16, 77, columnWidth + 6, height), InventorySourceHeight(source));
            inventoryPackScroll = InventoryColumn(inventoryPanel, "InventoryPack", new Rect(right - 8, 77, columnWidth + 6, height), state.pack.Count * 39);
            var left = inventorySourceScroll.content;
            var pack = inventoryPackScroll.content;
            inventorySearchText = null;
            float leftY = 0;
            if (scavengeRun.CanReadWarehouseTransferNote)
            {
                InventoryLabel(left, "NoteTitle", new Rect(8, 0, width - 8, 20), Loc.T("InventoryNoteTitle"), 13, LootWarning);
                InventoryLabel(left, "NoteText", new Rect(8, 20, width - 8, 24), Loc.T("InventoryNoteText"), 13, LootText, wrap: true);
                leftY = 46;
            }
            if (source == null) DrawInventoryEquipment(left, width, enabled);
            else if (searching || source.items.Count == 0)
            {
                var label = InventoryLabel(left, "SourceStatus", new Rect(8, leftY, width - 8, 38),
                    searching ? "" : Loc.T("InventoryEmpty"), 14, LootMuted);
                if (searching) inventorySearchText = label;
            }
            else DrawInventoryTransfers(left, source.items, state.pack, source, leftY, width, ScavengeSurvival.Capacity, true, enabled);
            if (state.pack.Count == 0)
                InventoryLabel(pack, "EmptyPack", new Rect(8, 0, width - 8, 38), Loc.T("InventoryPackEmpty"), 14, LootMuted);
            else if (source == null) DrawInventoryPackActions(pack, width, enabled);
            else DrawInventoryTransfers(pack, state.pack, source.items, source, 0, width, 12, false, enabled);
        }

        private bool InventoryCanMove(SurvivalContainer source) => CanShowInventoryCanvas() &&
            CurrentInventorySource == source && scavengeRun.SearchRemaining <= 0 &&
            scavengeRun.Survival.action == SurvivalAction.None && !scavengeRun.Survival.sleeping;

        /// <summary>满包时仍允许合并可堆叠物品，点击时再次验证当前来源。</summary>
        private void DrawInventoryTransfers(RectTransform parent, List<SurvivalStack> from, List<SurvivalStack> to,
            SurvivalContainer source, float y, float width, int capacity, bool take, bool enabled)
        {
            for (int i = 0; i < from.Count; i++)
            {
                var item = from[i];
                var row = InventoryItemRow(parent, item, y + i * 39, width, !take);
                var at = new Rect(take ? width - 68 : 4, 4, 64, 27);
                bool allowed = enabled && ScavengeSurvival.CanReceive(item, to, capacity, out _);
                InventoryButton(row, take ? "Take" : "Store", at, Loc.T(take ? "InventoryTake" : "InventoryStore"), allowed, () =>
                {
                    if (InventoryCanMove(source))
                        ScavengeSurvival.Transfer(from, from.IndexOf(item), to, capacity);
                });
            }
        }

        private void DrawInventoryPackActions(RectTransform parent, float width, bool enabled)
        {
            var pack = scavengeRun.Survival.pack;
            for (int i = 0; i < pack.Count; i++)
            {
                var item = pack[i];
                var row = InventoryItemRow(parent, item, i * 39, width, true);
                var action = ActionForSurvivalItem(item.item);
                bool equip = IsSurvivalEquipment(item.item);
                if (equip || action != SurvivalAction.None)
                    InventoryButton(row, "UseItem", new Rect(3, 4, 32, 27), Loc.T(equip ? "InventoryEquip" : "InventoryUse"), enabled, () =>
                    {
                        int index = pack.IndexOf(item);
                        if (index >= 0 && InventoryCanMove(null)) UseSurvivalPackItem(index, equip, action);
                    });
                InventoryButton(row, "DropItem", new Rect(37, 4, 32, 27), Loc.T("InventoryDrop"), enabled, () =>
                {
                    int index = pack.IndexOf(item);
                    if (index >= 0 && InventoryCanMove(null)) DropSurvivalPackItem(index);
                });
            }
        }

        private void DrawInventoryEquipment(RectTransform parent, float width, bool enabled)
        {
            var state = scavengeRun.Survival;
            void Slot(SurvivalStack item, bool torch, float y)
            {
                if (item == null)
                {
                    InventoryLabel(parent, "EmptyEquipment", new Rect(8, y, width - 8, 35),
                        Loc.T(torch ? "InventoryEmptyTorch" : "InventoryEmptyWeapon"), 14, LootMuted);
                    return;
                }
                var row = InventoryItemRow(parent, item, y, width, false);
                InventoryButton(row, "Stow", new Rect(width - 68, 4, 64, 27), Loc.T("InventoryStow"),
                    enabled && state.Used + item.Slots <= ScavengeSurvival.Capacity, () =>
                    {
                        if (InventoryCanMove(null)) state.Stow(torch);
                    });
            }
            Slot(state.weapon, false, 0);
            Slot(state.torch, true, 39);
            if (state.torch != null)
                InventoryButton(parent, "ReplaceBattery", new Rect(3, 82, 128, 27), Loc.T("InventoryReplaceBattery"), enabled,
                    () => { if (InventoryCanMove(null)) StartSurvivalAction(SurvivalAction.Battery); });
        }

        private RectTransform InventoryItemRow(Transform parent, SurvivalStack item, float y, float width, bool inPack)
        {
            var row = InventoryRect(parent, item.item.ToString(), new Rect(0, y, width, 35));
            InventoryGraphic(row, "Row", new Rect(0, 0, width, 35), new Color(.092f, .137f, .132f));
            float iconX = inPack ? 72 : 0;
            InventoryGraphic(row, "IconWell", new Rect(iconX, 0, 34, 35), new Color(.14f, .20f, .19f));
            var icon = LootTexture(item.item.ToString());
            if (icon) InventoryGraphic(row, "ItemIcon", new Rect(iconX + 3, 3, 28, 28), Color.white, icon);
            if (item.item is SurvivalItem.Crowbar or SurvivalItem.Pistol)
                InventoryLabel(row, "ItemType", new Rect(iconX + 3, 0, 28, 35),
                    Loc.T(item.item == SurvivalItem.Crowbar ? "InventoryTool" : "InventoryWeapon"), 11, LootMuted);
            string name = Loc.T("InventoryItem." + item.item);
            string label = item.item switch
            {
                SurvivalItem.Pistol => $"{name} {item.rounds}/{ScavengeSurvival.PistolMagazineCapacity}",
                SurvivalItem.Torch => $"{name} {item.charge / 3.6f:0}%",
                _ => item.count > 1 ? $"{name} ×{item.count}" : name
            };
            InventoryLabel(row, "ItemName", new Rect(iconX + 41, 0, width - 117, 35), label, 14, LootText, wrap: true);
            return row;
        }

        private void DrawInventoryCapacity(float x, int used)
        {
            const int capacity = ScavengeSurvival.Capacity;
            Color filled = used >= capacity ? LootWarning : new Color(.52f, .76f, .64f);
            for (int i = 0; i < capacity; i++)
            {
                var cell = new Rect(x + i * 16, 58, 13, 11);
                InventoryGraphic(inventoryPanel, "CapacityCell", cell, LootLine);
                InventoryGraphic(inventoryPanel, "CapacityFill", new Rect(cell.x + 1, cell.y + 1, 11, 9), i < used ? filled : LootBackground);
            }
            InventoryLabel(inventoryPanel, "Capacity", new Rect(x + capacity * 16 + 6, 51, 52, 25),
                $"{used}/{capacity}", 13, filled, bold: true);
        }

        private static RectTransform InventoryRect(Transform parent, string name, Rect rect)
        {
            var node = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            node.SetParent(parent, false);
            node.anchorMin = node.anchorMax = node.pivot = new Vector2(0, 1);
            node.anchoredPosition = new Vector2(rect.x, -rect.y);
            node.sizeDelta = rect.size;
            return node;
        }

        private static RawImage InventoryGraphic(Transform parent, string name, Rect bounds, Color color, Texture texture = null, bool hit = false)
        {
            var graphic = InventoryRect(parent, name, bounds).gameObject.AddComponent<RawImage>();
            graphic.texture = texture;
            graphic.color = color;
            graphic.raycastTarget = hit;
            return graphic;
        }

        private TMP_Text InventoryLabel(Transform parent, string name, Rect bounds, string value, int size, Color color,
            bool bold = false, bool wrap = false, bool centered = false)
        {
            var text = InventoryRect(parent, name, bounds).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = inventoryFont;
            text.text = value;
            text.color = color;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Min(11, size);
            text.fontSizeMax = size;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = centered ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = wrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private void InventoryButton(Transform parent, string name, Rect bounds, string value, bool enabled, Action clicked)
        {
            var graphic = InventoryGraphic(parent, name, bounds, Color.white, LootTexture("button"), true);
            var button = graphic.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = new Color(.27f, .38f, .34f);
            colors.highlightedColor = new Color(.42f, .58f, .48f);
            colors.pressedColor = new Color(.20f, .32f, .28f);
            colors.disabledColor = new Color(.16f, .18f, .17f);
            colors.fadeDuration = 0;
            button.colors = colors;
            button.interactable = enabled;
            button.onClick.AddListener(() => { if (CanShowInventoryCanvas()) clicked(); });
            InventoryLabel(graphic.transform, "Label", new Rect(1, 0, bounds.width - 2, bounds.height), value, 13,
                enabled ? LootText : LootMuted, centered: true);
        }

        private static ScrollRect InventoryColumn(Transform parent, string name, Rect bounds, float contentHeight)
        {
            var view = InventoryGraphic(parent, name, bounds, Color.clear, hit: true);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view.rectTransform;
            scroll.content = InventoryRect(view.transform, "Rows", new Rect(0, 0, bounds.width, Mathf.Max(bounds.height, contentHeight)));
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            if (contentHeight > bounds.height)
            {
                var track = InventoryGraphic(parent, name + "Scrollbar",
                    new Rect(bounds.xMax - 8, bounds.y, 6, bounds.height), new Color(.12f, .19f, .18f), hit: true);
                var handle = InventoryGraphic(track.transform, "Thumb", Rect.zero, LootMuted, hit: true);
                handle.rectTransform.anchorMin = Vector2.zero;
                handle.rectTransform.anchorMax = Vector2.one;
                handle.rectTransform.offsetMin = handle.rectTransform.offsetMax = Vector2.zero;
                var bar = track.gameObject.AddComponent<Scrollbar>();
                bar.direction = Scrollbar.Direction.BottomToTop;
                bar.handleRect = handle.rectTransform;
                bar.targetGraphic = handle;
                bar.navigation = new Navigation { mode = Navigation.Mode.None };
                scroll.verticalScrollbar = bar;
            }
            return scroll;
        }

        private Texture2D LootTexture(string name)
        {
            if (lootTextures.TryGetValue(name, out var texture)) return texture;
            texture = UnityEngine.Resources.Load<Texture2D>(InventoryTexturePath + name);
            lootTextures.Add(name, texture);
            return texture;
        }
    }
}
