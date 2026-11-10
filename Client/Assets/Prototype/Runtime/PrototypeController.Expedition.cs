using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 研究窗可以在战场持续推进时查看，回归确认单独冻结战场。
        private bool researchOpen, returnConfirm;
        private Transform clueMarker, surveyMarker;
        internal ExpeditionProfile ExpeditionProfile => session.Profile;

        internal void Depart(ExpeditionMap map)
        {
            if (!initialized || UtilityWindowOpen || Overlay != PrototypeOverlay.Start) return;
            var world = session.BeginExpedition(System.Guid.NewGuid().GetHashCode(), map);
            if (world == null) return;
            ShowWorld(world, true);
            RefreshOverlay();
        }

        internal void TrainCommander()
        {
            if (!initialized || UtilityWindowOpen || Overlay != PrototypeOverlay.Start) return;
            ShowFeedback(ExpeditionProfile.Train() ? "体能训练已完成" : "体能训练未保存");
            RefreshOverlay();
        }

        internal void ToggleResearch()
        {
            if (!initialized || UtilityWindowOpen || World.Expedition == null) return;
            if (researchOpen && Overlay == PrototypeOverlay.Research) researchOpen = false;
            else
            {
                if (Overlay != PrototypeOverlay.None || !World.NearShelter) return;
                CloseConstruction();
                World.HoldInteraction(false);
                researchOpen = true;
            }
            RefreshOverlay();
        }

        internal void Research(ExpeditionTech tech)
        {
            if (UtilityWindowOpen || Overlay != PrototypeOverlay.Research || !World.StartResearch(tech)) return;
            RefreshOverlay();
        }

        internal void ConfirmReturn()
        {
            if (!CanBuild || World.Expedition?.BasicComplete != true) return;
            CloseConstruction();
            returnConfirm = true;
            session.SetPaused(true);
            RefreshOverlay();
        }

        internal void CancelReturn()
        {
            if (UtilityWindowOpen || Overlay != PrototypeOverlay.ReturnConfirm) return;
            returnConfirm = false;
            World.HoldInteraction(Input.GetKey(KeyCode.E));
            session.SetPaused(false);
            RefreshOverlay();
        }

        private void CreateExpeditionMarkers(Material blue, Material amber)
        {
            clueMarker = surveyMarker = null;
            if (World.Expedition is not { } expedition) return;
            clueMarker = Shape("ResearchClue", worldRoot, PrimitiveType.Cube, At(expedition.CluePosition, .8f),
                new Vector3(1.3f, 1.3f, 1.3f), blue);
            clueMarker.localRotation = Quaternion.Euler(20, 45, 0);
            surveyMarker = Shape("SurveySite", worldRoot, PrimitiveType.Cylinder, At(expedition.SurveyPosition, .65f),
                new Vector3(2.4f, .65f, 2.4f), amber);
        }

        private void SyncExpeditionMarkers()
        {
            if (World.Expedition is not { } expedition) return;
            clueMarker.gameObject.SetActive(!expedition.FoundClue && World.Vision.IsVisible(expedition.CluePosition));
            surveyMarker.gameObject.SetActive(World.Vision.IsVisible(expedition.SurveyPosition));
            if (expedition.End == ExpeditionEnd.Died) commander.gameObject.SetActive(false);
        }

        private void DrawExpeditionStatus()
        {
            var expedition = World.Expedition;
            var run = World.Run;
            int remaining = Mathf.CeilToInt(Mathf.Max(0, expedition.Rules.SurvivalSeconds - run.Elapsed));
            string objective = expedition.BasicComplete ? "基础任务完成 · 可以回归" :
                $"存活 {expedition.Rules.Days} 天 · 剩余 {remaining / 60}:{remaining % 60:00}";
            string status = World.InteractionHint;
            if (string.IsNullOrEmpty(status) && expedition.Researching.HasValue)
                status = $"{ExpeditionState.TechName(expedition.Researching.Value)} · {Mathf.CeilToInt(expedition.ResearchLeft)} 秒";
            if (string.IsNullOrEmpty(status))
                status = World.NearShelter ? "T 打开研究" : "蓝色为线索 · 黄色为勘察点";
            GUI.Label(new Rect(30, 26, 305, 30), $"{expedition.Rules.Name} · {expedition.Rules.Era}");
            GUI.Label(new Rect(30, 61, 305, 28), $"生命 {run.Health}/{expedition.MaxHealth}     物资 {run.Funds}");
            GUI.Label(new Rect(30, 92, 305, 28), objective);
            GUI.Label(new Rect(30, 122, 305, 28), status);
        }
    }
}
