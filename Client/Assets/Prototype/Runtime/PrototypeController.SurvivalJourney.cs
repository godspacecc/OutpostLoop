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
        }

        /// <summary>确认和帮助期间暂停局时；关闭窗口的按键不再传给战场。</summary>
        private bool PollSurvivalJourney(Keyboard keyboard)
        {
            if (!survivalHelpOpen && !survivalConfirmExit) return false;
            if (keyboard?.escapeKey.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.Escape))
                ResetSurvivalJourney();
            return true;
        }

        /// <summary>常驻按钮和目标卡吞掉攻击输入，避免点击界面时挥击或开枪。</summary>
        private static bool SurvivalJourneyContains(Vector2 point, float width, float height) =>
            new Rect(18, height - 61, 286, 40).Contains(point) ||
            new Rect(410, 18, Mathf.Min(430, width - 650), 112).Contains(point);

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
                return "先找补给：靠近镇口容器按 E 搜索，点击拾取水和食品。";
            if (state.baseRoom < 0)
                return "活过三天：寻找商铺后屋或住宅。进屋可设为基地，也可轮换落脚。";
            return "活过三天：按需补水、进食和睡眠，留意夜间巡逻与基地入口。";
        }

        /// <summary>不阻断落地操作的目标说明、快捷入口及按权威剩余时间绘制的动作进度。</summary>
        private void DrawSurvivalJourney(float width, float height)
        {
            float cardWidth = Mathf.Min(430, width - 650);
            GUI.Box(new Rect(410, 18, cardWidth, 112), "");
            GUI.Label(new Rect(422, 24, cardWidth - 24, 26), "当前行动 · 在镇中生存三天");
            var wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUI.Label(new Rect(422, 52, cardWidth - 24, 70), SurvivalObjective(), wrapped);
            if (GUI.Button(new Rect(18, height - 61, 135, 40), "暂停  Esc")) scavengePaused = true;
            if (GUI.Button(new Rect(161, height - 61, 143, 40), "操作与目标")) survivalHelpOpen = true;

            var state = scavengeRun.Survival;
            if (state.action == SurvivalAction.None) return;
            float duration = SurvivalActionDuration(state.action);
            var bar = new Rect(width / 2 - 180, height - 113, 360, 7);
            ScavengeFill(bar, new Color(.12f, .16f, .18f));
            ScavengeFill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(1 - state.actionTime / duration), bar.height),
                new Color(.35f, .78f, .68f));
        }

        /// <summary>进度仅用于显示；操作时长仍由领域层决定。</summary>
        private static float SurvivalActionDuration(SurvivalAction action) => action switch
        {
            SurvivalAction.Drink => 2, SurvivalAction.Eat => 4, SurvivalAction.Bandage => 3,
            SurvivalAction.Aid => 6, SurvivalAction.Reload => 2.5f, SurvivalAction.Fortify => 20,
            SurvivalAction.Repair => 8, SurvivalAction.Alarm => 10, _ => 3
        };

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
            if (!survivalHelpOpen && !survivalConfirmExit) return false;
            ScavengeFill(new Rect(0, 0, width, height), new Color(.045f, .065f, .075f, .95f));
            float x = (width - 680) / 2, y = (height - 470) / 2;
            GUI.Box(new Rect(x, y, 680, 470), "");
            var wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUI.Label(new Rect(x + 24, y + 18, 632, 35), survivalConfirmExit ? "确认提前撤离" : "第一趟行动 · 操作与目标");
            string text = survivalConfirmExit
                ? $"当前存活 {SurvivalTime(scavengeRun.Survival.elapsed)}，尚未活满三天。\n\n现在撤离会结束本趟，并记录为目标未达成。结算会记录实际存活时间和撤离时的随身物品。\n\n也可以留在镇中继续生存，达标后再撤离。"
                : "目标：在镇中存活 72 分钟（三个游戏日），然后在 2 分钟窗口内到撤离点按 E。最后 5 分钟会提前提醒。\n\nWASD 移动，Shift 奔跑；E 搜索、开关门、睡觉或撤离。\nTab 打开背包，点击使用或装备；搜索界面可切到「使用 / 装备」。\n鼠标瞄准，左键攻击；R 装填，F 开关已装备的手电。\n进入商铺后屋或住宅可设基地；入口旁按 V 加固、维修或安装警报。\n\n饮食、治疗和施工完成才扣道具；移动、受击或 Esc 会中断。睡眠只恢复精力，世界仍继续推进。\nEsc 暂停，可保存并返回开始页，再用「继续游戏」恢复。\n小地图：浅黄为自己，青绿为撤离点，绿为基地，灰为已搜空容器。";
            GUI.Label(new Rect(x + 24, y + 63, 632, 316), text, wrapped);
            if (survivalConfirmExit)
            {
                if (GUI.Button(new Rect(x + 24, y + 402, 300, 42), "继续生存  Esc")) survivalConfirmExit = false;
                if (GUI.Button(new Rect(x + 350, y + 402, 306, 42), "提前撤离 · 目标未达成"))
                {
                    survivalConfirmExit = false;
                    if (scavengeRun.SurvivalExit()) CloseSurvivalSave();
                }
            }
            else if (GUI.Button(new Rect(x + 24, y + 402, 632, 42), scavengePaused ? "返回暂停菜单  Esc" : "返回行动  Esc"))
                survivalHelpOpen = false;
            return true;
        }
    }
}
