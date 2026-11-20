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

        /// <summary>结算只展示本趟状态，不把物品登记成空间库存。</summary>
        private string SurvivalResultDetails()
        {
            var state = scavengeRun.Survival;
            if (scavengeRun.Outcome == ScavengeOutcome.Died)
                return "生命耗尽，本趟结束。重新行动从镇口空手开始。\n提示：出血需用绷带处理，睡眠不能恢复生命。";
            if (scavengeRun.Outcome == ScavengeOutcome.WindowClosed)
                return "未在达标后的两分钟内撤离。重新行动从镇口空手开始。\n提示：最后五分钟会预告撤离，请提前向出口靠拢。";
            string carried = $"撤离时背包 {state.Used}/6 格 · 武器：{(state.weapon == null ? "无" : DescribeSurvivalItem(state.weapon))}";
            return carried + "\n本趟物资留在当地，空间不存放物品。再次行动空手开始。";
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
