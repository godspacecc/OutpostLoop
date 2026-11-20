using Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    /// <summary>显示河谷镇生存数据，预制体负责排版和基础样式。</summary>
    public sealed class SurvivalStatusView : MonoBehaviour
    {
        // 需求顺序固定为饮水、饱腹、精力。三张填充图使用径向填充。
        public RectTransform Panel;
        public TMP_Text Clock, Objective, HealthValue, HealthWarning, PackLabel, Weapon, Torch, Speed;
        public Image HealthFill;
        public Image WaterFill, FoodFill, EnergyFill;
        public TMP_Text WaterDetail, FoodDetail, EnergyDetail;
        public Button Pack;

        // 运行时将旧预制体控件拆到可独立定位的组，保留原来的文字和仪表绑定。
        private RectTransform action, equipment, pack;
        private RectTransform[] instruments;

        internal void InitializeLayout()
        {
            var root = (RectTransform)transform;
            action = Group(root, "ActionInfo");
            equipment = Group(root, "CurrentEquipment");
            pack = (RectTransform)Pack.transform;
            pack.SetParent(root, false);

            Move(Clock.rectTransform, action, 10, 3, 300);
            Move(Objective.rectTransform, action, 10, 30, 300);
            Move(Weapon.rectTransform, equipment, 10, 3, 220);
            Move(Torch.rectTransform, equipment, 10, 30, 220);
            Speed.rectTransform.SetParent(root, false);

            var life = Panel.Find("Life");
            var lifeTrack = (RectTransform)Panel.Find("LifeTrack");
            var water = (RectTransform)Panel.Find("Water");
            var food = (RectTransform)Panel.Find("Food");
            var energy = (RectTransform)Panel.Find("Energy");
            instruments = new[] { (RectTransform)life, lifeTrack, HealthValue.rectTransform, water, food, energy };
            foreach (RectTransform item in Panel)
            {
                if (item.name.EndsWith("Divider", System.StringComparison.Ordinal)) item.gameObject.SetActive(false);
                else item.anchoredPosition += new Vector2(0, 27);
            }
            Panel.GetComponent<Image>().raycastTarget = false;
        }

        private static RectTransform Group(Transform parent, string name) =>
            PrototypeMenuWidgets.Graphic(parent, name, 0, 0, 0, 0, new Color(.045f, .07f, .07f, .76f)).rectTransform;

        private static void Move(RectTransform child, RectTransform parent, float x, float y, float width)
        {
            child.SetParent(parent, false);
            child.anchoredPosition = new Vector2(x, -y);
            child.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }

        internal void ApplyLayout(Rect actionRect, Rect bodyRect, Rect equipmentRect, Rect packRect, Rect contextRect,
            bool showAction, bool showBody, bool showEquipment, bool showPack, bool showSpeed, ScavengeSurvival state)
        {
            bool warning = state.Ready || state.Warning;
            action.gameObject.SetActive(showAction || warning);
            Clock.gameObject.SetActive(showAction);
            Objective.gameObject.SetActive(showAction || warning);
            equipment.gameObject.SetActive(showEquipment && (state.weapon != null || state.torch != null));
            Panel.gameObject.SetActive(showBody || state.bleeding ||
                state.water < LowNeed || state.food < LowNeed || state.energy < LowNeed);
            // 低需求文字与图形同在一组。隐藏身体仪表时仅保留有预警的需求。
            instruments[0].gameObject.SetActive(showBody);
            instruments[1].gameObject.SetActive(showBody);
            instruments[2].gameObject.SetActive(showBody);
            instruments[3].gameObject.SetActive(showBody || state.water < LowNeed);
            instruments[4].gameObject.SetActive(showBody || state.food < LowNeed);
            instruments[5].gameObject.SetActive(showBody || state.energy < LowNeed);

            PerceptionGeometry.Place(action, actionRect, PerceptionGeometry.BaseSize(PerceptionGroup.Action));
            PerceptionGeometry.Place(Panel, bodyRect, PerceptionGeometry.BaseSize(PerceptionGroup.Body));
            PerceptionGeometry.Place(equipment, equipmentRect, PerceptionGeometry.BaseSize(PerceptionGroup.Equipment));
            PerceptionGeometry.Place(pack, packRect, packRect.size);
            pack.gameObject.SetActive(showPack);
            float speedScale = contextRect.width / PerceptionGeometry.BaseSize(PerceptionGroup.Interaction).x;
            var speedSize = new Vector2(140, 25);
            var speedRect = new Rect(contextRect.x + 10 * speedScale, contextRect.y, 140 * speedScale, 25 * speedScale);
            PerceptionGeometry.Place(Speed.rectTransform, speedRect, speedSize);
            Speed.gameObject.SetActive(showSpeed);
        }

        // 保留旧 HUD 的生存预警阈值。低生命颜色是显示反馈。
        private const float LowNeed = 25;
        private static readonly Color Critical = new(.98f, .62f, .52f);
        private static readonly Color Low = new(1f, .77f, .44f);
        private static readonly Color Normal = new(.62f, .84f, .76f);
        private static readonly Color Readout = new(.81f, .88f, .85f);

        // 仅在读取到新显示值时重新格式化文案，填充量可逐帧平滑变化。
        private (string Language, int Day, int Minute, int Remaining, bool Ready, bool Warning) timeText;
        private (string Language, int Water, int Food, int Energy, bool WaterLow, bool FoodLow,
            bool EnergyLow, bool Bleeding, bool Details) needText;
        private (string Language, bool Driving, bool PackOpen, int Used, SurvivalItem? WeaponId,
            int Rounds, int TorchCharge, int Kph) gearText;

        /// <summary>背包只切换数值详略。所有仪表直接读取当前这一趟的领域状态。</summary>
        internal void Render(ScavengeSurvival state, bool driving, float vehicleSpeed, bool packOpen)
        {
            string language = Game.Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value;
            HealthFill.rectTransform.anchorMax = new Vector2(Fraction(state.health), 1);
            HealthFill.color = state.bleeding || state.health < LowNeed ? Critical : Normal;
            HealthValue.SetText("{0:0}", Mathf.CeilToInt(Mathf.Max(0, state.health)));

            RenderNeed(WaterFill, state.water);
            RenderNeed(FoodFill, state.food);
            RenderNeed(EnergyFill, state.energy);
            RefreshNeeds(state, packOpen, language);
            RefreshTime(state, language);
            RefreshGear(state, driving, vehicleSpeed, packOpen, language);
        }

        /// <summary>温和的中性色显示常规余量，短标签与暖色一起提示低需求。</summary>
        private static void RenderNeed(Image fill, float value)
        {
            fill.fillAmount = Fraction(value);
            fill.color = value < LowNeed ? Low : Normal;
        }

        private static float Fraction(float value) => Mathf.Clamp01(value / 100f);

        private void RefreshNeeds(ScavengeSurvival state, bool details, string language)
        {
            var next = (Language: language,
                Water: details ? Mathf.RoundToInt(state.water) : 0,
                Food: details ? Mathf.RoundToInt(state.food) : 0,
                Energy: details ? Mathf.RoundToInt(state.energy) : 0,
                WaterLow: state.water < LowNeed, FoodLow: state.food < LowNeed, EnergyLow: state.energy < LowNeed,
                Bleeding: state.bleeding, Details: details);
            if (needText == next) return;
            needText = next;
            HealthWarning.text = state.bleeding ? Loc.T("SurvivalStatusBleeding") : "";
            NeedLabel(WaterDetail, "Water", next.Water, next.WaterLow, details);
            NeedLabel(FoodDetail, "Food", next.Food, next.FoodLow, details);
            NeedLabel(EnergyDetail, "Energy", next.Energy, next.EnergyLow, details);
        }

        private static void NeedLabel(TMP_Text label, string need, int amount, bool low, bool details)
        {
            label.color = low ? Low : Readout;
            label.text = details ? Loc.T("SurvivalStatus" + need + "Value", amount) :
                low ? Loc.T("SurvivalStatus" + need + "Low") : "";
        }

        private void RefreshTime(ScavengeSurvival state, string language)
        {
            int day = (int)(state.elapsed / ScavengeSurvival.Day) + 1;
            int minute = Mathf.FloorToInt(state.Hour * 60);
            int remaining = Mathf.CeilToInt((float)System.Math.Max(0,
                (state.Ready ? ScavengeSurvival.Deadline : ScavengeSurvival.Goal) - state.elapsed));
            var next = (language, day, minute, remaining, state.Ready, state.Warning);
            if (timeText == next) return;
            timeText = next;
            Clock.text = Loc.T("SurvivalStatusClock", day, minute / 60, minute % 60);
            string key = state.Ready ? "SurvivalStatusExtraction" :
                state.Warning ? "PerceptionExtractionSoon" : "SurvivalStatusRemaining";
            Objective.text = Loc.T(key, remaining / 60, remaining % 60);
            Objective.color = state.Ready || state.Warning ? Low : Readout;
        }

        private void RefreshGear(ScavengeSurvival state, bool driving, float vehicleSpeed, bool packOpen, string language)
        {
            var next = (Language: language, Driving: driving, PackOpen: packOpen, Used: state.Used,
                WeaponId: state.weapon?.item, Rounds: state.weapon?.rounds ?? 0,
                TorchCharge: state.torch == null ? -1 : Mathf.RoundToInt(state.torch.charge / 3.6f),
                Kph: driving ? Mathf.RoundToInt(Mathf.Abs(vehicleSpeed) * 3.6f) : 0);
            if (gearText == next) return;
            gearText = next;
            Speed.text = driving ? Loc.T("SurvivalStatusSpeed", next.Kph) : "";
            PackLabel.text = packOpen ? Loc.T("SurvivalStatusPackOpen") :
                Loc.T("SurvivalStatusPack", next.Used, ScavengeSurvival.Capacity);
            Torch.text = driving || next.TorchCharge < 0 ? "" : Loc.T("SurvivalStatusLight", next.TorchCharge);
            string weaponName = next.WeaponId.HasValue ? Loc.T("InventoryItem." + next.WeaponId.Value) : "";
            Weapon.text = next.WeaponId == SurvivalItem.Pistol
                ? $"{weaponName} {next.Rounds}/{ScavengeSurvival.PistolMagazineCapacity}" : weaponName;
        }
    }
}
