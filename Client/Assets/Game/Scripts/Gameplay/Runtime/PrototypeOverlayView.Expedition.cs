using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    internal sealed partial class PrototypeOverlayView
    {
        // 局内窗口只呈现领域状态，实际扣费、任务和保存由系统处理。
        private Text cancelReturn, researchStatus, shelterOption, dismantleBarricade;
        private GameObject researchRows;
        private readonly Text[] techActions = new Text[ExpeditionState.TechTree.Length];

        private void BuildExpeditionModal(Transform panel, Font font, PrototypeController controller)
        {
            researchRows = Rect("ResearchRows", panel, new Vector2(.5f, .5f), new Vector2(680, 286), new Vector2(0, 14)).gameObject;
            Label(researchRows.transform, font, "FortificationBranch", new Vector2(310, 34), new Vector2(-170, 139), 24).text = "工事";
            Label(researchRows.transform, font, "HaulingBranch", new Vector2(310, 34), new Vector2(170, 139), 24).text = "采运";
            foreach (var tech in ExpeditionState.TechTree)
            {
                int index = (int)tech;
                var position = tech switch
                {
                    ExpeditionTech.Fortifications => new Vector2(-170, 91),
                    ExpeditionTech.Reinforcement => new Vector2(-170, 27),
                    ExpeditionTech.Bracing => new Vector2(-170, -37),
                    ExpeditionTech.Handcart => new Vector2(170, 91),
                    ExpeditionTech.Pulley => new Vector2(170, 27),
                    _ => new Vector2(0, -101)
                };
                techActions[index] = Button(researchRows.transform, font, "Research" + tech, "",
                    new Vector2(tech == ExpeditionTech.Survey ? 660 : 322, 58), position, new Vector2(.5f, .5f), () => controller.Research(tech));
                techActions[index].fontSize = 18;
            }
            Label(researchRows.transform, font, "WorkshopRequirement", new Vector2(322, 58), new Vector2(170, -37), 18).text =
                "旧工坊 · 带回野外测绘\n山口烽台 · 开闸回归";
            researchStatus = Label(researchRows.transform, font, "ResearchStatus", new Vector2(660, 37), new Vector2(0, -151), 21);
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
                        ExpeditionEnd.Surveyed => "终极任务完成 · 自动回归",
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
            var rect = (RectTransform)option.transform.parent;
            float y = kind switch { ExpansionKind.Extractor => 66, ExpansionKind.Barricade => 22, ExpansionKind.Turret => -22, _ => 110 };
            rect.anchoredPosition = new Vector2(0, y);
            string requirement = world.BuildingRequirement(kind);
            bool unlocked = requirement.Length == 0;
            string label = $"{ExpeditionBuildingName(kind)} · {spec.Cost} 物资 / {spec.BuildSeconds} 秒";
            if (!unlocked) label += " · " + requirement;
            SetText(option, controller.PlacementKind == kind ? "已选 · " + label : label);
            SetAffordable(option, unlocked && world.Run.CanAfford(spec.Cost));
        }

        private static string ExpeditionBuildingName(ExpansionKind kind) => kind switch
        {
            ExpansionKind.Extractor => "采运棚",
            ExpansionKind.Barricade => "木桩拒马",
            ExpansionKind.Turret => "绊索弩台",
            _ => "庇护所"
        };
    }
}
