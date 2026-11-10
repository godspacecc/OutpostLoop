using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    internal sealed partial class PrototypeOverlayView
    {
        // 开局与局内窗口只呈现领域状态，实际扣费、任务和保存由系统处理。
        private Text profileSummary, legacyBalance, training, industrialEntry, trainingFeedback;
        private Text cancelReturn, researchStatus, shelterOption;
        private GameObject researchRows;
        private readonly Text[] techActions = new Text[3];

        private void BuildDeparture(Font font, PrototypeController controller, Color ink, Color mint, Color muted, Color line)
        {
            StartText(font, "ExpeditionTitle", "探索出发", 96, 165, 650, 76, 59, ink, FontStyle.Bold);
            StartText(font, "ExpeditionSubtitle", "活着回归，将资金和科技带回档案", 96, 245, 650, 44, 26, muted);
            Fill("ProfileRule", 96, 305, 650, 2, line);
            profileSummary = Label(StartRect("ExpeditionProfile", 96, 318, 650, 72), font, "Label",
                new Vector2(650, 72), Vector2.zero, 25);
            profileSummary.alignment = TextAnchor.MiddleLeft;

            BuildMapEntry(font, controller, ExpeditionMap.Wilderness, 418, mint, muted);
            industrialEntry = BuildMapEntry(font, controller, ExpeditionMap.Industrial, 571, mint, muted);

            training = Button(startStage, font, "TrainCommander", "", new Vector2(650, 54), new Vector2(421, -747),
                new Vector2(0, 1), controller.TrainCommander);
            training.fontSize = 24;
            trainingFeedback = Label(StartRect("TrainingFeedback", 96, 777, 650, 38), font, "Label",
                new Vector2(650, 38), Vector2.zero, 22);

            Fill("LegacyRule", 96, 824, 650, 2, line);
            var legacy = Button(startStage, font, "PrimaryAction", Game.Loc.T("PreviewText12"), new Vector2(260, 60),
                new Vector2(226, -879), new Vector2(0, 1), controller.ActOnModal);
            legacy.fontSize = 25;
            legacyBalance = Label(StartRect("StartingBalance", 380, 844, 366, 70), font, "Label",
                new Vector2(366, 70), Vector2.zero, 21);
            legacyBalance.alignment = TextAnchor.MiddleLeft;
        }

        private Text BuildMapEntry(Font font, PrototypeController controller, ExpeditionMap map, float y, Color mint, Color muted)
        {
            var rules = ExpeditionMapRules.For(map);
            var label = Button(startStage, font, "Depart" + map, $"{rules.Name} · {rules.Era}  →", new Vector2(650, 58),
                new Vector2(421, -y), new Vector2(0, 1), () => controller.Depart(map));
            label.fontSize = 29;
            StartText(font, map + "Basic", $"基础 · 存活 {rules.Days} 天（{rules.SurvivalSeconds / 60} 分钟）  +{rules.BasicFunds} 资金",
                96, y + 36, 650, 35, 25, mint);
            StartText(font, map + "Survey", $"终极 · {rules.Objective}  +{rules.SurveyFunds} 资金",
                96, y + 74, 650, 35, 25, muted);
            return label;
        }

        private void PresentDeparture(PrototypeController controller)
        {
            var profile = controller.ExpeditionProfile;
            SetText(profileSummary, $"局外资金 {profile.Funds}  ·  指挥员生命 {profile.MaxHealth}\n已带回科技 · {ExpeditionState.DescribeTech(profile.Tech)}");
            bool unlocked = profile.CanEnter(ExpeditionMap.Industrial);
            SetText(industrialEntry, unlocked ? "旧工业区 · 工事时代  →" : "旧工业区 · 工事时代  /  需野外测绘");
            SetAffordable(industrialEntry, unlocked);
            SetText(training, profile.Trained ? "体能训练 · 已完成" :
                $"体能训练 · {ExpeditionProfile.TrainingCost} 资金 · 下次出发生命 +20");
            SetAffordable(training, profile.CanTrain);
            SetText(trainingFeedback, controller.ActionFeedback);
            SetText(legacyBalance, Game.Loc.T("StartingFundsTier", controller.World.Run.Funds) + "\n" + TierProgress(controller));
        }

        private void BuildExpeditionModal(Transform panel, Font font, PrototypeController controller)
        {
            researchRows = Rect("ResearchRows", panel, new Vector2(.5f, .5f), new Vector2(680, 286), new Vector2(0, 14)).gameObject;
            foreach (var tech in ExpeditionState.TechTree)
            {
                int index = (int)tech;
                techActions[index] = Button(researchRows.transform, font, "Research" + tech, "", new Vector2(660, 64),
                    new Vector2(0, 103 - index * 72), new Vector2(.5f, .5f), () => controller.Research(tech));
                techActions[index].fontSize = 22;
            }
            researchStatus = Label(researchRows.transform, font, "ResearchStatus", new Vector2(660, 48), new Vector2(0, -106), 22);
            cancelReturn = Button(panel, font, "CancelReturn", "取消 · 继续探索", new Vector2(390, 48), new Vector2(0, -113),
                new Vector2(.5f, .5f), controller.CancelReturn);
        }

        private void PresentExpeditionModal(PrototypeController controller)
        {
            var world = controller.World;
            var expedition = world.Expedition;
            switch (controller.Overlay)
            {
                case PrototypeOverlay.Paused:
                    SetText(heading, "探索暂停");
                    bool abandoning = controller.ConfirmingRestart;
                    SetText(details, abandoning ? "放弃会清空本局物资、任务和新研究。" : "继续后恢复本局计时。");
                    restartAction.transform.parent.gameObject.SetActive(!expedition.BasicComplete);
                    SetText(restartAction, abandoning ? "确认放弃本局" : "放弃本局");
                    SetText(actionLabel, "继续探索");
                    break;
                case PrototypeOverlay.Research:
                    details.gameObject.SetActive(false);
                    SetText(heading, "庇护所研究");
                    SetText(actionLabel, "返回战场");
                    foreach (var tech in ExpeditionState.TechTree)
                    {
                        var label = techActions[(int)tech];
                        string blocked = world.ResearchBlock(tech);
                        string prerequisite = ExpeditionState.Requirement(tech);
                        SetAffordable(label, blocked.Length == 0);
                        label.color = LabelColor;
                        SetText(label, $"{ExpeditionState.TechName(tech)}   {ExpeditionState.TechCost(tech)} 物资 / {ExpeditionState.TechSeconds(tech)} 秒" +
                            $"\n前置 · {prerequisite}{(blocked.Length == 0 || blocked == prerequisite ? "" : "   |   " + blocked)}");
                    }
                    SetText(researchStatus, expedition.Researching.HasValue
                        ? $"{ExpeditionState.TechName(expedition.Researching.Value)} 研究中 · 剩余 {Mathf.CeilToInt(expedition.ResearchLeft)} 秒"
                        : $"可用物资 {world.Run.Funds}");
                    break;
                case PrototypeOverlay.ReturnConfirm:
                    SetText(heading, "确认回归");
                    SetText(details, $"基础任务奖励 · {expedition.Rules.BasicFunds} 资金\n本局可带回科技 · {ExpeditionState.DescribeTech(expedition.NewTech)}");
                    SetText(actionLabel, "确认回归");
                    break;
                case PrototypeOverlay.Result:
                    bool pending = expedition.Reward > 0 && !controller.VictoryRewardRecorded;
                    SetText(heading, expedition.End switch
                    {
                        ExpeditionEnd.Surveyed => "完成勘察 · 自动回归",
                        ExpeditionEnd.Returned => "探索回归",
                        ExpeditionEnd.Abandoned => "已放弃探索",
                        _ => "指挥员阵亡"
                    });
                    SetText(details, expedition.Reward == 0 ? "本局物资、任务奖励与新研究已清空。" :
                        pending ? "探索档案尚未写入，请重试保存。" :
                        $"已入账 · {expedition.Reward} 资金\n已带回 · {ExpeditionState.DescribeTech(expedition.NewTech)}\n局外余额 · {controller.ExpeditionProfile.Funds}");
                    SetText(actionLabel, pending ? "重试保存" : "返回出发页");
                    break;
            }
        }

        private static void PresentExpeditionBuilding(PrototypeController controller, Text option, ExpansionKind kind)
        {
            var world = controller.World;
            var spec = world.BuildingSpec(kind);
            bool turret = kind == ExpansionKind.Turret;
            var rect = (RectTransform)option.transform.parent;
            rect.anchoredPosition = new Vector2(0, turret ? 66 : 110);
            bool unlocked = !turret || world.Expedition.Has(ExpeditionTech.Fortifications);
            string name = turret ? "简易炮塔" : "庇护所";
            string label = $"{name} · {spec.Cost} 物资 / {spec.BuildSeconds} 秒";
            if (!unlocked) label += " · 需要简易工事";
            SetText(option, controller.PlacementKind == kind ? "已选 · " + label : label);
            SetAffordable(option, unlocked && world.Run.CanAfford(spec.Cost));
        }
    }
}
