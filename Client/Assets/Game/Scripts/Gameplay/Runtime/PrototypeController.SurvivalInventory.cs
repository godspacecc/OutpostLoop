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
        private const string InventoryPrefabAddress = "Prefabs/UI/RiverValleyInventory";
        private const string InventoryTexturePath = "RiverValleyInventory/";
        private readonly Dictionary<string, Texture2D> lootTextures = new();
        private readonly StringBuilder inventorySnapshot = new();
        private GameObject inventoryRoot;
        private SurvivalInventoryView inventoryView;
        // 单独记录行实例，刷新时保留所属列其余控件和滚动位置。
        private readonly List<SurvivalInventoryRowView> inventoryRows = new();
        private bool inventoryPrefabLoadFailed;
        private string inventoryRevision;
        private SurvivalContainer inventoryDisplayedSource;
        private float inventoryDisplayedHeight;
        private static readonly Color LootBackground = new(.052f, .078f, .078f, .97f);
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
            int rows = source == null || scavengeRun.SearchRemaining > 0 ? 1 : Mathf.Max(1, source.items.Count);
            return rows * InventoryRowPitch + (scavengeRun.CanReadWarehouseTransferNote ? 46 : 0);
        }

        private bool CanShowInventoryCanvas() => CanShowSurvivalHud() && SurvivalInventoryOpen;

        /// <summary>物品和交互共用模态窗口的可见性判定。</summary>
        private bool CanShowSurvivalHud() => initialized && !crossing && survivalMode && scavengeRoot != null &&
            scavengeRoot.gameObject.activeSelf && scavengeRun != null &&
            scavengeRun.Outcome == ScavengeOutcome.Exploring && !scavengePaused && !survivalHelpOpen &&
            !survivalConfirmExit && !survivalRegionMapOpen && !RiverValleyPortalPlaying && !UtilityWindowOpen;

        /// <summary>局内面板跟随实际开关和模态窗口，只在内容或语言有变化时重排。</summary>
        private void LateUpdate()
        {
            UpdateSurvivalContext();
            UpdateSurvivalStatus();
            if (!CanShowInventoryCanvas())
            {
                if (inventoryRoot && inventoryRoot.activeSelf) inventoryRoot.SetActive(false);
                inventoryRevision = null;
                return;
            }
            if (!inventoryRoot && !LoadInventoryView()) return;

            inventoryRoot.SetActive(true);
            inventoryView.Panel.gameObject.SetActive(!survivalPackOpen);
            if (survivalPackOpen)
            {
                UpdateInventoryManagement();
                inventoryRevision = null;
                return;
            }
            if (managementRoot) managementRoot.gameObject.SetActive(false);

            float height = SurvivalInventory(Screen.width / HudScale, Screen.height / HudScale).height;
            var source = CurrentInventorySource;
            string revision = InventoryRevision(source);
            if (revision != inventoryRevision || inventoryDisplayedSource != source || inventoryDisplayedHeight != height)
            {
                bool preserveScroll = inventoryRevision != null && inventoryDisplayedSource == source;
                float left = preserveScroll ? inventoryView.Source.verticalNormalizedPosition : 1;
                float right = preserveScroll ? inventoryView.Pack.verticalNormalizedPosition : 1;
                RefreshInventory(height, source);
                Canvas.ForceUpdateCanvases();
                inventoryView.Source.verticalNormalizedPosition = left;
                inventoryView.Pack.verticalNormalizedPosition = right;
                inventoryRevision = revision;
                inventoryDisplayedSource = source;
                inventoryDisplayedHeight = height;
            }
        }

        /// <summary>通过项目 UI 资源地址加载面板，固定控件只接线一次。</summary>
        private bool LoadInventoryView()
        {
            if (inventoryPrefabLoadFailed) return false;
            var prefab = ResourceLoader.LoadAsset<GameObject>(InventoryPrefabAddress);
            if (!prefab || !prefab.TryGetComponent<SurvivalInventoryView>(out var template))
            {
                inventoryPrefabLoadFailed = true;
                Debug.LogError("河谷镇物品页预制体缺失：" + InventoryPrefabAddress, this);
                return false;
            }

            inventoryView = Instantiate(template, transform, false);
            inventoryRoot = inventoryView.gameObject;
            inventoryRoot.name = "RiverValleyInventoryCanvas";
            if (!inventoryView.Frame.texture)
                inventoryView.Frame.texture = LootTexture("frame");
            inventoryView.Frame.enabled = inventoryView.Frame.texture != null;
            ApplyInventoryButtonTexture(inventoryView.Equipment);
            ApplyInventoryButtonTexture(inventoryView.Close);
            inventoryView.Equipment.gameObject.SetActive(true);
            var packShortcut = (RectTransform)inventoryView.Equipment.transform;
            packShortcut.anchoredPosition = new Vector2(462, packShortcut.anchoredPosition.y);
            packShortcut.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 56);
            var shortcutLabel = inventoryView.Equipment.GetComponentInChildren<TMP_Text>(true).rectTransform;
            shortcutLabel.anchorMin = Vector2.zero;
            shortcutLabel.anchorMax = Vector2.one;
            shortcutLabel.offsetMin = shortcutLabel.offsetMax = Vector2.zero;
            var titleRect = inventoryView.Title.rectTransform;
            titleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 433);
            inventoryView.Equipment.onClick.AddListener(() => { if (CanShowInventoryCanvas()) OpenSurvivalPack(); });
            inventoryView.Close.onClick.AddListener(() => { if (CanShowInventoryCanvas()) CloseSurvivalPanels(); });
            return true;
        }

        /// <summary>世界输入命中使用预制体的实际位置。</summary>
        private bool InventoryContains(Vector2 cursor, Vector2 hudPoint, float width, float height)
        {
            if (!SurvivalPanelOpen) return false;
            if (survivalPackOpen) return true;
            if (SurvivalInventoryOpen && inventoryView && inventoryView.isActiveAndEnabled)
                return RectTransformUtility.RectangleContainsScreenPoint(inventoryView.Panel, cursor);
            return SurvivalInventory(width, height).Contains(hudPoint);
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

        /// <summary>紧凑页的左列绑定当前来源，右列始终显示随身背包。</summary>
        private void RefreshInventory(float height, SurvivalContainer source)
        {
            var state = scavengeRun.Survival;
            var view = inventoryView;
            bool searching = scavengeRun.SearchRemaining > 0;
            bool enabled = InventoryCanMove(source);
            bool showNote = scavengeRun.CanReadWarehouseTransferNote;

            foreach (var row in inventoryRows)
            {
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            inventoryRows.Clear();

            view.Panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            string title = Loc.T(state.drops.Contains(source) ? "InventoryGround" : "InventoryStorage");
            if (scavengeRun.OpenCache >= 0)
            {
                string name = scavengeRun.CacheNames[scavengeRun.OpenCache];
                string key = "InventorySource." + name;
                title = Loc.T(key);
                if (title == key) title = name;
            }
            view.Title.text = title;
            view.SourceHeading.text = Loc.T("InventoryLoot");
            view.Equipment.GetComponentInChildren<TMP_Text>(true).text = "Tab";
            UpdateInventoryCapacity(state.Used);

            ResizeInventoryColumn(view.Source, InventorySourceHeight(source));
            ResizeInventoryColumn(view.Pack, state.pack.Count * InventoryRowPitch);
            view.Note.gameObject.SetActive(showNote);
            view.EmptyWeapon.SetActive(false);
            view.EmptyTorch.SetActive(false);
            view.ReplaceBattery.gameObject.SetActive(false);
            view.EmptyPack.SetActive(state.pack.Count == 0);

            bool showStatus = !searching && source.items.Count == 0;
            view.SourceStatus.gameObject.SetActive(showStatus);
            float offset = showNote ? view.Note.rect.height : 0;
            var status = view.SourceStatus.rectTransform;
            status.anchoredPosition = new Vector2(status.anchoredPosition.x, -offset);
            if (showStatus) view.SourceStatus.text = Loc.T("InventoryEmpty");

            if (!searching && !showStatus)
                DrawInventoryTransfers(source.items, state.pack, source, offset, ScavengeSurvival.Capacity, true, enabled);
            DrawInventoryTransfers(state.pack, source.items, source, 0, 12, false, enabled);
        }

        // 行间距沿用物品页的 35 像素行和 4 像素留白。
        private const float InventoryRowPitch = 39;

        private static void ResizeInventoryColumn(ScrollRect scroll, float contentHeight)
        {
            scroll.StopMovement();
            scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(InventoryRowPitch, contentHeight));
        }

        private bool InventoryCanMove(SurvivalContainer source) => CanShowInventoryCanvas() &&
            CurrentInventorySource == source && scavengeRun.SearchRemaining <= 0 &&
            scavengeRun.Survival.action == SurvivalAction.None && !scavengeRun.Survival.sleeping;

        /// <summary>满包时仍允许合并可堆叠物品，点击时再次验证当前来源。</summary>
        private void DrawInventoryTransfers(List<SurvivalStack> from, List<SurvivalStack> to,
            SurvivalContainer source, float y, int capacity, bool take, bool enabled)
        {
            for (int i = 0; i < from.Count; i++)
            {
                var item = from[i];
                var row = InventoryItemRow(item, y + i * InventoryRowPitch, !take);
                row.Transfer.name = take ? "Take" : "Store";
                bool allowed = enabled && ScavengeSurvival.CanReceive(item, to, capacity, out _);
                BindInventoryButton(row.Transfer, take ? "InventoryTake" : "InventoryStore", allowed, () =>
                {
                    if (InventoryCanMove(source))
                        ScavengeSurvival.Transfer(from, from.IndexOf(item), to, capacity);
                });
            }
        }

        private SurvivalInventoryRowView InventoryItemRow(SurvivalStack item, float y, bool inPack)
        {
            var template = inPack ? inventoryView.PackRow : inventoryView.SourceRow;
            var scroll = inPack ? inventoryView.Pack : inventoryView.Source;
            var row = Instantiate(template, scroll.content, false);
            inventoryRows.Add(row);
            row.name = item.item.ToString();
            var rect = (RectTransform)row.transform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y - y);
            if (inPack)
            {
                row.Use.gameObject.SetActive(false);
                row.Drop.gameObject.SetActive(false);
            }
            row.Icon.texture = LootTexture(row.name);
            row.Icon.enabled = row.Icon.texture != null;
            row.ItemType.text = item.item switch
            {
                SurvivalItem.Crowbar => Loc.T("InventoryTool"),
                SurvivalItem.Pistol => Loc.T("InventoryWeapon"),
                _ => ""
            };
            string name = Loc.T("InventoryItem." + item.item);
            row.Name.text = item.item switch
            {
                SurvivalItem.Pistol => $"{name} {item.rounds}/{ScavengeSurvival.PistolMagazineCapacity}",
                SurvivalItem.Torch => $"{name} {item.charge / 3.6f:0}%",
                _ => item.count > 1 ? $"{name} ×{item.count}" : name
            };
            return row;
        }

        private void UpdateInventoryCapacity(int used)
        {
            const int capacity = ScavengeSurvival.Capacity;
            Color filled = used >= capacity ? LootWarning : new Color(.52f, .76f, .64f);
            for (int i = 0; i < inventoryView.CapacityFills.Length; i++)
                inventoryView.CapacityFills[i].color = i < used ? filled : LootBackground;
            inventoryView.Capacity.text = $"{used}/{capacity}";
            inventoryView.Capacity.color = filled;
        }

        private void BindInventoryButton(Button button, string key, bool enabled, Action clicked)
        {
            if (key != null) button.GetComponentInChildren<TMP_Text>(true).text = Loc.T(key);
            ApplyInventoryButtonTexture(button);
            SetInventoryButtonState(button, enabled);
            button.onClick.AddListener(clicked.Invoke);
            button.gameObject.SetActive(true);
        }

        private static void SetInventoryButtonState(Button button, bool enabled)
        {
            button.interactable = enabled;
            button.GetComponentInChildren<TMP_Text>(true).color = enabled ? LootText : LootMuted;
        }

        private void ApplyInventoryButtonTexture(Button button)
        {
            if (button.targetGraphic is RawImage graphic && !graphic.texture)
                graphic.texture = LootTexture("button");
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
