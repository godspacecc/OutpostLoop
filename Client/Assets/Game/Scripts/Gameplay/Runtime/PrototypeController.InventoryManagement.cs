using System;
using Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        private enum ManagementPage { Pack, Gear, Status, Map, Tasks, Journal }
        private static readonly string[] ManagementTabs =
            { "InventoryPack", "ManagementGear", "ManagementStatus", "ManagementMap", "ManagementTasks", "ManagementJournal" };
        private static readonly Color ManagementPaper = new(.071f, .096f, .097f);
        private static readonly Color ManagementCard = new(.103f, .135f, .136f);
        private static readonly Color ManagementSlot = new(.143f, .181f, .180f);
        private static readonly Color ManagementAccent = new(.84f, .68f, .43f);
        private readonly Button[] managementTabs = new Button[ManagementTabs.Length];

        // 当前页面、选中物品和预览纹理只属于界面。
        private RectTransform managementRoot, managementSheet, managementContent;
        private TMP_Text managementTitle, managementClose, managementClock, managementTimer, managementWarning;
        private Texture2D managementPortrait;
        private ManagementPage managementPage;
        private SurvivalStack managementSelection, managementDisplayedSelection;
        private string managementRevision;
        private int managementStatusRevision;

        /// <summary>沿用物品预制体的 Canvas 和字体，按实际视口缩放独立管理页。</summary>
        private void UpdateInventoryManagement()
        {
            if (!managementRoot) CreateInventoryManagement();
            managementRoot.gameObject.SetActive(true);
            var size = managementRoot.rect.size;
            managementSheet.localScale = Vector3.one * Mathf.Min(1f, size.x / 1160f, size.y / 660f);
            var state = scavengeRun.Survival;
            int day = (int)(state.elapsed / ScavengeSurvival.Day) + 1;
            int minute = Mathf.FloorToInt(state.Hour * 60);
            int seconds = Mathf.CeilToInt((float)Math.Max(0, (state.Ready ? ScavengeSurvival.Deadline : ScavengeSurvival.Goal) - state.elapsed));
            string clock = Loc.T("SurvivalStatusClock", day, minute / 60, minute % 60);
            string timer = Loc.T(state.Ready ? "SurvivalStatusExtraction" : "SurvivalStatusRemaining", seconds / 60, seconds % 60);
            managementClock.text = clock;
            managementTimer.text = timer;
            managementWarning.text = state.bleeding ? Loc.T("SurvivalStatusBleeding") : "";

            string revision = InventoryRevision(null);
            int status = 0;
            if (managementPage == ManagementPage.Status)
                status = Mathf.RoundToInt(state.health) + 101 * (Mathf.RoundToInt(state.water) + 101 *
                    (Mathf.RoundToInt(state.food) + 101 * Mathf.RoundToInt(state.energy)));
            else if (managementPage == ManagementPage.Tasks)
                status = state.Ready ? 2 : state.Warning ? 1 : 0;
            if (revision == managementRevision && managementDisplayedSelection == managementSelection &&
                managementStatusRevision == status) return;
            managementRevision = revision;
            managementStatusRevision = status;
            RefreshInventoryManagement(state);
            managementDisplayedSelection = managementSelection;
        }

        private void CreateInventoryManagement()
        {
            var parent = (RectTransform)inventoryRoot.transform;
            managementRoot = ManagementRect(parent, "Management", 0, 0, 0, 0);
            managementRoot.anchorMin = Vector2.zero;
            managementRoot.anchorMax = Vector2.one;
            managementRoot.offsetMin = managementRoot.offsetMax = Vector2.zero;
            managementRoot.gameObject.AddComponent<Image>().color = new Color(.028f, .046f, .052f, .96f);
            managementSheet = ManagementRect(managementRoot, "Sheet", 0, 0, 1120, 622);
            managementSheet.anchorMin = managementSheet.anchorMax = managementSheet.pivot = new Vector2(.5f, .5f);
            managementSheet.anchoredPosition = Vector2.zero;
            managementSheet.gameObject.AddComponent<Image>().color = ManagementPaper;
            ManagementBlock(managementSheet, new Rect(0, 0, 1120, 3), ManagementAccent);
            managementTitle = ManagementText(managementSheet, new Rect(26, 12, 490, 52), 32, LootText);
            var close = ManagementButton(managementSheet, "CloseManagement", new Rect(970, 20, 124, 38), CloseSurvivalPanels);
            managementClose = close.GetComponentInChildren<TMP_Text>();
            managementClock = ManagementText(managementSheet, new Rect(26, 576, 282, 26), 17, LootMuted);
            managementTimer = ManagementText(managementSheet, new Rect(318, 576, 370, 26), 17, ManagementAccent);
            managementWarning = ManagementText(managementSheet, new Rect(704, 576, 390, 26), 17, LootWarning, TextAlignmentOptions.Right);
            for (int i = 0; i < managementTabs.Length; i++)
            {
                var page = (ManagementPage)i;
                managementTabs[i] = ManagementButton(managementSheet, "Tab" + page,
                    new Rect(26 + i * 180, 76, 168, 42), () =>
                    {
                        if (!CanShowInventoryCanvas() || !survivalPackOpen) return;
                        if (page == ManagementPage.Map) { OpenRiverValleyRegion(); return; }
                        managementPage = page;
                        managementRevision = null;
                    });
            }
        }

        /// <summary>根据真实背包、装备和行动进度填充当前页。</summary>
        private void RefreshInventoryManagement(ScavengeSurvival state)
        {
            managementTitle.text = Loc.T("ManagementRegion");
            managementClose.text = Loc.T("ManagementClose");
            for (int i = 0; i < managementTabs.Length; i++)
            {
                var tab = managementTabs[i];
                bool active = (int)managementPage == i;
                tab.image.color = active ? ManagementAccent : ManagementSlot;
                var label = tab.GetComponentInChildren<TMP_Text>();
                label.color = active ? ManagementPaper : LootText;
                label.text = Loc.T(ManagementTabs[i]);
            }
            if (managementContent)
            {
                managementContent.gameObject.SetActive(false);
                Destroy(managementContent.gameObject);
            }
            managementContent = ManagementRect(managementSheet, "Page" + managementPage, 26, 136, 1068, 422);

            if (managementPage == ManagementPage.Status)
                DrawManagementStatus(state);
            else if (managementPage == ManagementPage.Tasks)
                DrawManagementTasks(state);
            else if (managementPage == ManagementPage.Journal)
                DrawManagementJournal();
            else
                DrawManagementItems(state);
        }

        private void DrawManagementItems(ScavengeSurvival state)
        {
            var portrait = ManagementBlock(managementContent, new Rect(0, 0, 274, 422), ManagementCard);
            var grid = ManagementBlock(managementContent, new Rect(286, 0, 426, 422), ManagementCard);
            var detail = ManagementBlock(managementContent, new Rect(724, 0, 344, 422), ManagementCard);
            ManagementText(portrait, new Rect(16, 8, 240, 39), 20, LootText).text = Loc.T("InventoryEquipped");
            var image = ManagementRect(portrait, "CharacterPreview", 16, 50, 242, 226).gameObject.AddComponent<RawImage>();
            image.texture = InventoryCharacterPortrait();
            image.raycastTarget = false;
            DrawManagementGearSlot(portrait, state.weapon, 288, "ManagementWeaponSlot");
            DrawManagementGearSlot(portrait, state.torch, 350, "ManagementLightSlot");

            bool gear = managementPage == ManagementPage.Gear;
            ManagementText(grid, new Rect(16, 8, 290, 39), 20, LootText).text =
                Loc.T(gear ? "ManagementAvailableGear" : "InventoryPack");
            ManagementText(grid, new Rect(303, 8, 107, 39), 20,
                state.Used >= ScavengeSurvival.Capacity ? LootWarning : ManagementAccent, TextAlignmentOptions.Right).text =
                $"{state.Used}/{ScavengeSurvival.Capacity}";

            int occupied = 0;
            foreach (var stack in state.pack)
            {
                if (gear && !IsSurvivalEquipment(stack.item)) continue;
                DrawManagementGridSlot(grid, occupied, stack, false);
                for (int i = 1; i < stack.Slots; i++) DrawManagementGridSlot(grid, occupied + i, stack, true);
                occupied += stack.Slots;
            }
            for (int i = occupied; i < ScavengeSurvival.Capacity; i++)
                DrawManagementGridSlot(grid, i, null, false);
            if (occupied == 0)
                ManagementText(grid, new Rect(16, 354, 394, 44), 17, LootMuted).text =
                    Loc.T(gear ? "ManagementNoGear" : "InventoryPackEmpty");

            if (managementSelection != null && !state.pack.Contains(managementSelection) &&
                managementSelection != state.weapon && managementSelection != state.torch)
                managementSelection = null;
            DrawManagementDetail(detail, state, managementSelection);
        }

        private void DrawManagementGearSlot(RectTransform parent, SurvivalStack item, float y, string key)
        {
            var bounds = new Rect(16, y, 242, 54);
            var row = item == null ? ManagementBlock(parent, bounds, ManagementSlot) :
                (RectTransform)ManagementButton(parent, key, bounds, () => managementSelection = item, false).transform;
            if (item != null && managementSelection == item) row.GetComponent<Image>().color = new Color(.32f, .28f, .19f);
            ManagementText(row, new Rect(12, 2, 220, 21), 13, LootMuted).text = Loc.T(key);
            ManagementText(row, new Rect(12, 22, 220, 27), 17, LootText).text = item == null ?
                Loc.T("InventoryEmpty") : ManagementItemName(item);
        }

        private void DrawManagementGridSlot(RectTransform parent, int index, SurvivalStack item, bool continuation)
        {
            var bounds = new Rect(16 + index % 3 * 134, 60 + index / 3 * 144, 126, 134);
            if (item == null)
            {
                var empty = ManagementBlock(parent, bounds, ManagementPaper);
                ManagementText(empty, new Rect(0, 0, 126, 134), 22, LootMuted, TextAlignmentOptions.Center).text = "–";
                return;
            }
            var cell = ManagementButton(parent, "Slot" + index, bounds, () => managementSelection = item, false);
            var rect = (RectTransform)cell.transform;
            cell.image.color = item == managementSelection ? new Color(.32f, .28f, .19f) : ManagementSlot;
            if (item == managementSelection)
                ManagementBlock(rect, new Rect(0, 0, 126, 2), ManagementAccent);
            if (continuation)
                ManagementText(rect, new Rect(0, 38, 126, 42), 23, LootMuted, TextAlignmentOptions.Center).text = "↔";
            else
                ManagementItemIcon(rect, new Rect(31, 12, 64, 66), item);
            string label = continuation ? Loc.T("ManagementSlotsUsed", item.Slots) : ManagementItemName(item);
            ManagementText(rect, new Rect(5, 80, 116, 46), 16, LootText, TextAlignmentOptions.Center).text = label;
        }

        /// <summary>同一物品可能在换装后仍被选中，操作按它现在所在的位置重新绑定。</summary>
        private void DrawManagementDetail(RectTransform parent, ScavengeSurvival state, SurvivalStack item)
        {
            ManagementText(parent, new Rect(18, 8, 308, 39), 20, LootText).text = Loc.T("ManagementDetail");
            if (item == null)
            {
                ManagementText(parent, new Rect(18, 66, 308, 44), 17, LootMuted).text = Loc.T("ManagementSelect");
                return;
            }
            ManagementItemIcon(parent, new Rect(114, 57, 116, 95), item);
            ManagementText(parent, new Rect(18, 159, 308, 63), 23, LootText, TextAlignmentOptions.Center).text =
                ManagementItemName(item);
            bool equipped = item == state.weapon || item == state.torch;
            ManagementText(parent, new Rect(18, 227, 308, 27), 16, ManagementAccent, TextAlignmentOptions.Center).text =
                equipped ? Loc.T("InventoryEquipped") : Loc.T("ManagementItemNumbers", item.count, item.StackLimit, item.Slots);
            ManagementText(parent, new Rect(18, 255, 308, 38), 16, LootMuted, TextAlignmentOptions.Center).text =
                Loc.T("ManagementItem." + item.item);

            bool ready = InventoryCanMove(null);
            if (equipped)
            {
                bool light = item == state.torch;
                ManagementAction(parent, "Stow", "ManagementStow", 366, ready && state.Used + item.Slots <= ScavengeSurvival.Capacity, () =>
                {
                    if (InventoryCanMove(null) && item == (light ? state.torch : state.weapon)) state.Stow(light);
                });
                return;
            }

            bool equip = IsSurvivalEquipment(item.item);
            var action = item.item switch
            {
                SurvivalItem.Ammo => SurvivalAction.Reload,
                SurvivalItem.Battery => SurvivalAction.Battery,
                _ => ActionForSurvivalItem(item.item)
            };
            var actionKey = action switch
            {
                SurvivalAction.Reload => "ManagementReload",
                SurvivalAction.Battery => "InventoryReplaceBattery",
                _ => equip ? "InventoryEquip" : "InventoryUse"
            };
            if (equip || action != SurvivalAction.None)
                ManagementAction(parent, "UseSelected", actionKey, 320, ready, () =>
                {
                    int index = state.pack.IndexOf(item);
                    if (index < 0 || !InventoryCanMove(null)) return;
                    UseSurvivalPackItem(index, equip, action);
                    // 规则拒绝时保留选择，让玩家看到原因。
                    if (!equip && survivalPackOpen)
                    {
                        var feedback = parent.Find("ActionFeedback").GetComponent<TMP_Text>();
                        feedback.text = Loc.T("ManagementBlocked." + action);
                    }
                });
            ManagementText(parent, new Rect(18, 292, 308, 25), 15, LootWarning, TextAlignmentOptions.Center).name = "ActionFeedback";
            ManagementAction(parent, "DropSelected", "InventoryDrop", 366, ready, () =>
            {
                int index = state.pack.IndexOf(item);
                if (index >= 0 && InventoryCanMove(null)) DropSurvivalPackItem(index);
            });
        }

        private void ManagementAction(RectTransform parent, string name, string key, float y, bool enabled, Action clicked)
        {
            var button = ManagementButton(parent, name, new Rect(18, y, 308, 37), clicked);
            button.GetComponentInChildren<TMP_Text>().text = Loc.T(key);
            button.interactable = enabled;
        }

        private static string ManagementItemName(SurvivalStack item)
        {
            string name = Loc.T("InventoryItem." + item.item);
            return item.item switch
            {
                SurvivalItem.Pistol => $"{name}  {item.rounds}/{ScavengeSurvival.PistolMagazineCapacity}",
                SurvivalItem.Torch => $"{name}  {item.charge / 3.6f:0}%",
                _ => item.count > 1 ? $"{name} ×{item.count}" : name
            };
        }

        private void ManagementItemIcon(RectTransform parent, Rect bounds, SurvivalStack item)
        {
            var texture = LootTexture(item.item.ToString());
            if (texture)
            {
                float scale = Mathf.Min(bounds.width / texture.width, bounds.height / texture.height);
                var size = new Vector2(texture.width, texture.height) * scale;
                var origin = bounds.center - size / 2;
                var icon = ManagementRect(parent, "ItemIcon", origin.x, origin.y, size.x, size.y)
                    .gameObject.AddComponent<RawImage>();
                icon.texture = texture;
                icon.raycastTarget = false;
            }
            else
            {
                ManagementText(parent, bounds, 23, ManagementAccent, TextAlignmentOptions.Center).text =
                    Loc.T(item.item == SurvivalItem.Pistol ? "InventoryWeapon" : "InventoryTool");
            }
        }

        private void DrawManagementStatus(ScavengeSurvival state)
        {
            var card = ManagementBlock(managementContent, new Rect(0, 0, 1068, 422), ManagementCard);
            ManagementText(card, new Rect(24, 17, 800, 44), 26, LootText).text = Loc.T("ManagementStatus");
            DrawManagementMeter(card, 24, 86, Loc.T("ManagementHealth", Mathf.RoundToInt(state.health)), state.health, state.bleeding);
            DrawManagementMeter(card, 552, 86, Loc.T("SurvivalStatusWaterValue", Mathf.RoundToInt(state.water)), state.water);
            DrawManagementMeter(card, 24, 234, Loc.T("SurvivalStatusFoodValue", Mathf.RoundToInt(state.food)), state.food);
            DrawManagementMeter(card, 552, 234, Loc.T("SurvivalStatusEnergyValue", Mathf.RoundToInt(state.energy)), state.energy);
            if (state.bleeding)
                ManagementText(card, new Rect(24, 361, 1000, 38), 20, LootWarning).text = Loc.T("ManagementBleeding");
        }

        private void DrawManagementMeter(RectTransform card, float x, float y, string label, float value, bool critical = false)
        {
            var meter = ManagementBlock(card, new Rect(x, y, 492, 118), ManagementSlot);
            ManagementText(meter, new Rect(18, 10, 456, 45), 25, LootText).text = label;
            ManagementBlock(meter, new Rect(18, 75, 456, 12), ManagementPaper);
            ManagementBlock(meter, new Rect(18, 75, 456 * Mathf.Clamp01(value / 100), 12),
                critical || value < 25 ? LootWarning : new Color(.52f, .76f, .64f));
        }

        private void DrawManagementTasks(ScavengeSurvival state)
        {
            var card = ManagementBlock(managementContent, new Rect(0, 0, 1068, 422), ManagementCard);
            ManagementText(card, new Rect(24, 17, 1018, 42), 22, LootMuted).text = Loc.T("ManagementCurrentTask");
            ManagementText(card, new Rect(24, 76, 1018, 75), 37, LootText).text =
                Loc.T(state.Ready ? "ManagementTaskExit" : "ManagementTaskSurvive");
            ManagementText(card, new Rect(24, 162, 810, 94), 22, LootMuted).text =
                Loc.T(state.Ready ? "ManagementTaskExitText" : state.Warning ? "ManagementTaskWarning" : "ManagementTaskSurviveText");
            var map = ManagementButton(card, "TaskMap", new Rect(24, 326, 196, 46), () =>
            {
                if (CanShowInventoryCanvas() && survivalPackOpen) OpenRiverValleyRegion();
            });
            map.GetComponentInChildren<TMP_Text>().text = Loc.T("ManagementMap");
        }

        private void DrawManagementJournal()
        {
            var card = ManagementBlock(managementContent, new Rect(0, 0, 1068, 422), ManagementCard);
            var progress = JourneyProgress;
            ManagementText(card, new Rect(24, 17, 1018, 42), 26, LootText).text = Loc.T("ManagementJournal");
            if (progress.visitedSpace && progress.hasResult)
            {
                ManagementText(card, new Rect(24, 83, 1018, 50), 23, ManagementAccent).text =
                    Loc.T("ManagementOutcome." + progress.lastOutcome);
                ManagementText(card, new Rect(24, 139, 1018, 46), 21, LootText).text =
                    Loc.T("ManagementLastRun", SurvivalTime(progress.survived), progress.carriedSlots, ScavengeSurvival.Capacity);
            }
            else
                ManagementText(card, new Rect(24, 83, 1018, 50), 21, LootMuted).text = Loc.T("ManagementNoRecords");

            if (scavengeRun.FullTown != null && scavengeRun.Survival.caches[RiverValley.RiverValleyWorldGeometry.TransferNoteCache].searched)
            {
                ManagementText(card, new Rect(24, 224, 1018, 42), 22, ManagementAccent).text = Loc.T("InventoryNoteTitle");
                ManagementText(card, new Rect(24, 272, 1018, 56), 19, LootText).text = Loc.T("InventoryNoteText");
            }
        }

        /// <summary>从项目现有玩家预制体拍摄静态立绘，关闭临时相机和模型后只保留纹理。</summary>
        private Texture2D InventoryCharacterPortrait()
        {
            if (managementPortrait) return managementPortrait;
            const int layer = 31;
            var stage = new GameObject("InventoryPortraitStage");
            stage.transform.position = new Vector3(0, -1000, 0);
            stage.SetActive(false);
            var model = Instantiate(LoadScavengeHuman(), stage.transform, false);
            foreach (var child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.enabled = false;
            camera.cullingMask = 1 << layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.058f, .083f, .086f);
            camera.orthographic = true;
            camera.farClipPlane = 12;
            var light = new GameObject("Key").AddComponent<Light>();
            light.transform.SetParent(stage.transform, false);
            light.type = LightType.Point;
            light.range = 8;
            light.intensity = 2.8f;
            light.color = new Color(1f, .88f, .71f);
            light.cullingMask = 1 << layer;
            light.transform.localPosition = new Vector3(-1.7f, 2.8f, 2.5f);
            var target = RenderTexture.GetTemporary(484, 452, 24);
            var previous = RenderTexture.active;
            camera.targetTexture = target;
            try
            {
                stage.SetActive(true);
                var animator = model.GetComponentInChildren<Animator>();
                if (animator) animator.Update(0);
                var renderers = model.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                camera.orthographicSize = bounds.extents.y * 1.15f;
                camera.transform.position = bounds.center + new Vector3(1.2f, .24f, 3.4f);
                camera.transform.LookAt(bounds.center);
                camera.Render();
                RenderTexture.active = target;
                managementPortrait = new Texture2D(target.width, target.height, TextureFormat.RGB24, false)
                    { name = "RiverValleyCharacterPortrait" };
                managementPortrait.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                managementPortrait.Apply(false, true);
            }
            finally
            {
                stage.SetActive(false);
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Destroy(stage);
            }
            return managementPortrait;
        }

        private static RectTransform ManagementRect(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private static RectTransform ManagementBlock(RectTransform parent, Rect bounds, Color color)
        {
            var rect = ManagementRect(parent, "Surface", bounds.x, bounds.y, bounds.width, bounds.height);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private TMP_Text ManagementText(RectTransform parent, Rect bounds, int size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var text = ManagementRect(parent, "Label", bounds.x, bounds.y, bounds.width, bounds.height)
                .gameObject.AddComponent<TextMeshProUGUI>();
            text.font = inventoryView.Title.font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private Button ManagementButton(RectTransform parent, string name, Rect bounds, Action clicked, bool withLabel = true)
        {
            var rect = ManagementRect(parent, name, bounds.x, bounds.y, bounds.width, bounds.height);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ManagementSlot;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            button.onClick.AddListener(clicked.Invoke);
            if (withLabel)
                ManagementText(rect, new Rect(5, 0, bounds.width - 10, bounds.height), 17, LootText, TextAlignmentOptions.Center);
            return button;
        }
    }
}
