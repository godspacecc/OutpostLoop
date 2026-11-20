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

        // 保留旧 HUD 的生存预警阈值。低生命颜色是显示反馈。
        private const float LowNeed = 25;
        private static readonly Color Critical = new(.98f, .62f, .52f);
        private static readonly Color Low = new(1f, .77f, .44f);
        private static readonly Color Normal = new(.62f, .84f, .76f);
        private static readonly Color Readout = new(.81f, .88f, .85f);

        // 仅在读取到新显示值时重新格式化文案，填充量可逐帧平滑变化。
        private (string Language, int Day, int Minute, int Remaining, bool Ready) timeText;
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
            var next = (language, day, minute, remaining, state.Ready);
            if (timeText == next) return;
            timeText = next;
            Clock.text = Loc.T("SurvivalStatusClock", day, minute / 60, minute % 60);
            Objective.text = Loc.T(state.Ready ? "SurvivalStatusExtraction" : "SurvivalStatusRemaining", remaining / 60, remaining % 60);
            Objective.color = state.Ready ? Low : Readout;
        }

        private void RefreshGear(ScavengeSurvival state, bool driving, float vehicleSpeed, bool packOpen, string language)
        {
            var next = (Language: language, Driving: driving, PackOpen: packOpen, Used: state.Used,
                WeaponId: state.weapon?.item, Rounds: state.weapon?.rounds ?? 0,
                TorchCharge: state.torch == null ? -1 : Mathf.RoundToInt(state.torch.charge / 3.6f),
                Kph: driving ? Mathf.RoundToInt(Mathf.Abs(vehicleSpeed) * 3.6f) : 0);
            if (gearText == next) return;
            gearText = next;
            Pack.gameObject.SetActive(!driving);
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
