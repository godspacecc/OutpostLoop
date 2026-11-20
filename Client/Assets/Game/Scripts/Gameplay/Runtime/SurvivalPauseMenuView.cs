using System;
using Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static OutpostLoop.PrototypeMenuWidgets;

namespace OutpostLoop
{
    /// <summary>河谷镇暂停与帮助的共用呈现。活动窗口由行动控制器裁决。</summary>
    internal sealed class SurvivalPauseMenuView : MonoBehaviour
    {
        private const int Pause = 0;
        private const int Help = 1;
        private RectTransform pausePanel, helpPanel;
        private Image shade;
        private TMP_Text pauseTitle, helpTitle, goal, helpNote, error;
        private Button resume, help, settings, leave, back;
        private TMP_Text[] helpRows;
        private Func<int> activePage;
        private Func<string> saveError;
        private string shownLanguage;
        private int shownPage = -1;

        /// <summary>暂停分支与帮助共享遮罩，页面关闭后控制权回到已有行动流程。</summary>
        internal void Initialize(Func<int> page, Func<string> errorText, Action onResume, Action onHelp,
            Action onSettings, Action onLeave, Action onBack)
        {
            activePage = page;
            saveError = errorText;
            var widgets = new PrototypeMenuWidgets();
            shade = Graphic(transform, "Shade", 0, 0, 0, 0, Shade, true);
            Stretch(shade.rectTransform);
            pausePanel = Panel(shade.transform, "PausePanel", 448, 339);
            pauseTitle = widgets.Label(pausePanel, "Title", 24, 17, 400, 42, "", 29, TextColor);
            Graphic(pausePanel, "Rule", 24, 65, 400, 1, Line);
            resume = widgets.Button(pausePanel, "ResumeSurvival", 24, 80, 400, 43, "", onResume, 19, true);
            settings = widgets.Button(pausePanel, "PauseSettings", 24, 132, 400, 43, "", onSettings, 19);
            help = widgets.Button(pausePanel, "PauseHelp", 24, 184, 400, 43, "", onHelp, 19);
            leave = widgets.Button(pausePanel, "SaveAndLeaveSurvival", 24, 236, 400, 43, "", onLeave, 19);
            error = widgets.Label(pausePanel, "SaveError", 24, 287, 400, 42, "", 15, Warning, true);

            helpPanel = Panel(shade.transform, "HelpPanel", 700, 518);
            helpTitle = widgets.Label(helpPanel, "Title", 24, 15, 652, 40, "", 27, TextColor);
            Graphic(helpPanel, "Rule", 24, 59, 652, 1, Line);
            goal = widgets.Label(helpPanel, "Goal", 24, 73, 652, 49, "", 18, TextColor, true);
            helpRows = new TMP_Text[HelpRows.Length];
            for (int i = 0; i < HelpRows.Length; i++)
            {
                float y = 128 + i * 44;
                Graphic(helpPanel, "Row", 24, y, 652, 40, Well);
                widgets.Label(helpPanel, "Keys", 36, y, 169, 40, HelpRows[i].Keys, 15, Muted);
                helpRows[i] = widgets.Label(helpPanel, "Action", 207, y, 452, 40, "", 16, TextColor);
            }
            helpNote = widgets.Label(helpPanel, "ActionTiming", 24, 400, 652, 49, "", 15, Muted, true);
            back = widgets.Button(helpPanel, "BackFromHelp", 500, 463, 176, 37, "", onBack, 16);
            Refresh();
        }

        // 帮助页只列当前已有的键鼠操作，提前撤离仍从原确认窗口进入。
        private static readonly (string Keys, string Label)[] HelpRows =
        {
            ("WASD / Shift", "PauseHelpMove"),
            ("E", "PauseHelpInteract"),
            ("Tab", "PauseHelpBackpack"),
            ("LMB / R / F", "PauseHelpCombat"),
            ("V", "PauseHelpBase"),
            ("M / Esc", "PauseHelpMap")
        };

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (!shade) return;
            int page = activePage();
            if (page != shownPage)
            {
                shownPage = page;
                shade.gameObject.SetActive(page >= 0);
                pausePanel.gameObject.SetActive(page == Pause);
                helpPanel.gameObject.SetActive(page == Help);
            }
            if (page < 0)
            {
                shade.gameObject.SetActive(false);
                return;
            }

            string language = Game.Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value;
            if (language != shownLanguage)
            {
                shownLanguage = language;
                pauseTitle.text = Loc.T("PauseRiverValleyTitle");
                Caption(resume, Loc.T("PauseRiverValleyResume"));
                Caption(settings, Loc.T("SettingsTitle"));
                Caption(help, Loc.T("PauseHelpTitle"));
                Caption(leave, Loc.T("PauseRiverValleySave"));
                helpTitle.text = Loc.T("PauseHelpTitle");
                goal.text = Loc.T("PauseHelpGoal");
                helpNote.text = Loc.T("PauseHelpTiming");
                for (int i = 0; i < helpRows.Length; i++) helpRows[i].text = Loc.T(HelpRows[i].Label);
                Caption(back, Loc.T("CommonBack") + "  Esc");
            }
            error.text = saveError();
        }
    }
}
