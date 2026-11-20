using Game;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 此处只装配界面，继续与保存仍执行现有行动逻辑。
        private SurvivalPauseMenuView survivalPauseMenu;

        private int CurrentSurvivalPausePage()
        {
            if (!isActiveAndEnabled || !initialized || crossing || !survivalMode || scavengeRun == null ||
                scavengeRoot == null || !scavengeRoot.gameObject.activeSelf || RiverValleyPortalPlaying ||
                scavengeRun.Outcome != ScavengeOutcome.Exploring || survivalConfirmExit ||
                survivalRegionMapOpen || UtilityWindowOpen) return -1;
            if (survivalHelpOpen) return 1;
            return scavengePaused ? 0 : -1;
        }

        private void OpenSurvivalSettings()
        {
            if (CurrentSurvivalPausePage() != 0) return;
            var settings = UIHelper.ShowUI<PrototypeSettingsCtrl>();
            if (settings?.IsShow() == true) settings.EnterFromSurvival();
        }

        /// <summary>局内菜单和既有地图、确认及结算的显示互斥。</summary>
        private void UpdateSurvivalPauseMenu()
        {
            if (survivalPauseMenu || CurrentSurvivalPausePage() < 0) return;

            var root = new GameObject("RiverValleyPauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            survivalPauseMenu = root.AddComponent<SurvivalPauseMenuView>();
            survivalPauseMenu.Initialize(CurrentSurvivalPausePage, () => survivalSaveError,
                () => { if (CurrentSurvivalPausePage() == 0) scavengePaused = false; },
                () => { if (CurrentSurvivalPausePage() == 0) survivalHelpOpen = true; },
                OpenSurvivalSettings,
                () => { if (CurrentSurvivalPausePage() == 0) SaveAndLeaveSurvival(); },
                () => { if (CurrentSurvivalPausePage() == 1) survivalHelpOpen = false; });
        }
    }
}
