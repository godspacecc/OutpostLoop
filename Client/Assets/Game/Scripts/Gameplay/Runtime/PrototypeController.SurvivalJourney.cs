using UnityEngine;
using UnityEngine.InputSystem;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 帮助与提前撤离确认仅属于界面，不写入行动存档或改变领域规则。
        private bool survivalHelpOpen, survivalConfirmExit;

        /// <summary>每次新行动或续玩清空暂态窗口。</summary>
        private void ResetSurvivalJourney()
        {
            survivalHelpOpen = survivalConfirmExit = false;
            survivalRegionMapOpen = false;
        }

        /// <summary>确认和帮助期间暂停局时；关闭窗口的按键不再传给战场。</summary>
        private bool PollSurvivalJourney(Keyboard keyboard, bool pausePressed)
        {
            bool mapPressed = keyboard?.mKey.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.M);
            if (mapPressed && !pausePressed && !survivalHelpOpen && !survivalConfirmExit && !scavengePaused)
            {
                if (survivalRegionMapOpen) survivalRegionMapOpen = false;
                else OpenRiverValleyRegion();
                return true;
            }
            if (survivalRegionMapOpen)
            {
                if (keyboard?.escapeKey.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.Escape))
                    survivalRegionMapOpen = false;
                return true;
            }
            if (!survivalHelpOpen && !survivalConfirmExit) return false;
            if (keyboard?.escapeKey.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.Escape))
                ResetSurvivalJourney();
            return true;
        }

        /// <summary>物品面板打开时缩小目标卡，绘制与输入拦截使用相同范围。</summary>
        private Rect SurvivalObjectiveRect(float width)
        {
            float height = SurvivalInventoryOpen ? 38 : 112;
            return new Rect(410, 18, Mathf.Min(430, width - 650), height);
        }

        /// <summary>根据真实身体状态和行动阶段给出下一步提示，不提前展示世界观背景。</summary>
        private string SurvivalObjective()
        {
            var state = scavengeRun.Survival;
            if (state.Ready) return "目标已达成：前往地图上的青绿色撤离点，按 E 撤离。";
            if (state.Warning) return "即将活满三天：带好饮水和医疗用品，向撤离点靠拢。";
            if (state.bleeding) return "你正在出血：找掩护，在背包使用绷带；急救包不能止血。";
            if (state.water < 25) return "饮水不足：按 Tab 使用密封饮水；没有饮水就搜索附近容器。";
            if (state.food < 25) return "需要进食：按 Tab 使用包装食品。";
            if (state.energy < 25) return "需要休息：找有床的安全房间，走近床按 E 睡觉。";
            if (state.pack.Count == 0 && state.weapon == null && state.torch == null)
                return "先找补给：WASD 移动，靠近镇口容器按 E 搜索，点击拾取水和食品。";
            if (state.elapsed < 120 && state.baseRoom < 0)
                return "已找到物品：按 Tab 查看背包，点击使用或装备；暂时不用的物品可留着。";
            if (state.baseRoom < 0)
                return "活过三天：寻找商铺后屋或住宅。进屋可设为基地，也可轮换落脚。";
            return "活过三天：按需补水、进食和睡眠，留意夜间巡逻与基地入口。";
        }

        /// <summary>在行动中显示当前目标，物品面板打开时只保留标题。</summary>
        private void DrawSurvivalObjective(float width)
        {
            var card = SurvivalObjectiveRect(width);
            GUI.Box(card, "");
            GUI.Label(new Rect(422, 24, card.width - 24, 26), "河谷镇 · 在镇中生存三天");
            if (card.height > 38)
            {
                var wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true };
                GUI.Label(new Rect(422, 52, card.width - 24, 70), SurvivalObjective(), wrapped);
            }
        }

        /// <summary>结算只展示本趟状态，不把物品登记成空间库存。</summary>
        private string SurvivalResultDetails()
        {
            var state = scavengeRun.Survival;
            if (scavengeRun.Outcome == ScavengeOutcome.Died)
                return "生命耗尽，本趟结束。重新行动从镇口空手开始。\n提示：出血需用绷带处理，睡眠不能恢复生命。";
            if (scavengeRun.Outcome == ScavengeOutcome.WindowClosed)
                return "未在达标后的两分钟内撤离。重新行动从镇口空手开始。\n提示：最后五分钟会预告撤离，请提前向出口靠拢。";
            string carried = $"撤离时背包 {state.Used}/6 格 · 武器：{(state.weapon == null ? "无" : DescribeSurvivalItem(state.weapon))}";
            return carried + "\n本趟物资与基地留在当地，空间不存放物品。再次行动空手开始。";
        }

        /// <summary>主动查看帮助或提前撤离时给出明确后果；关闭后恢复原有暂停状态。</summary>
        private bool DrawSurvivalJourneyModal(float width, float height)
        {
            if (survivalRegionMapOpen) { DrawRiverValleyRegion(width, height); return true; }
            if (!survivalConfirmExit) return scavengeRun.Outcome == ScavengeOutcome.Exploring && (scavengePaused || survivalHelpOpen);
            ScavengeFill(new Rect(0, 0, width, height), new Color(.045f, .065f, .075f, .95f));
            float x = (width - 680) / 2, y = (height - 470) / 2;
            GUI.Box(new Rect(x, y, 680, 470), "");
            var wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUI.Label(new Rect(x + 24, y + 18, 632, 35), "确认提前撤离");
            string text = $"当前存活 {SurvivalTime(scavengeRun.Survival.elapsed)}，尚未活满三天。\n\n现在撤离会结束本趟，并记录为目标未达成。结算会记录实际存活时间和撤离时的随身物品。\n\n也可以留在镇中继续生存，达标后再撤离。";
            GUI.Label(new Rect(x + 24, y + 63, 632, 316), text, wrapped);
            if (GUI.Button(new Rect(x + 24, y + 402, 300, 42), "继续生存  Esc")) survivalConfirmExit = false;
            if (GUI.Button(new Rect(x + 350, y + 402, 306, 42), "提前撤离 · 目标未达成"))
            {
                survivalConfirmExit = false;
                if (scavengeRun.SurvivalExit()) CloseSurvivalSave();
            }
            return true;
        }
    }
}
