using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 研究窗可以在战场持续推进时查看，回归确认单独冻结战场。
        private bool researchOpen, returnConfirm;
        private Transform clueMarker, surveyMarker, axleMarker, ropeMarker, objectiveMarker, gateMarker, beaconFlame;
        private readonly System.Collections.Generic.List<Transform> terrainMarkers =
            new System.Collections.Generic.List<Transform>();
        private int barricadeDismantle = -1;
        internal bool ConfirmingDismantle => SelectedBuilding >= 0 && barricadeDismantle == SelectedBuilding;
        internal bool ExpeditionIntroDismissed { get; private set; }
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

        internal void TrainScout()
        {
            if (!initialized || UtilityWindowOpen || Overlay != PrototypeOverlay.Start) return;
            ShowFeedback(ExpeditionProfile.TrainScout() ? "侦察训练已完成" : "侦察训练未保存");
            RefreshOverlay();
        }

        internal void DismissExpeditionIntro()
        {
            ExpeditionIntroDismissed = true;
            RefreshOverlay();
        }

        internal void DismantleBarricade()
        {
            if (!CanBuild || HasPlacement || World.Expedition == null || SelectedBuilding < 0 ||
                World.Buildings[SelectedBuilding].Kind != ExpansionKind.Barricade) return;
            if (!ConfirmingDismantle) barricadeDismantle = SelectedBuilding;
            else if (World.DismantleExpeditionBarricade(SelectedBuilding))
            {
                SelectedBuilding = barricadeDismantle = -1;
                ShowFeedback("木桩拒马已拆除");
            }
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

        private void CreateExpeditionMarkers(Material blue, Material amber, Material stone, Material timber)
        {
            terrainMarkers.Clear();
            clueMarker = surveyMarker = axleMarker = ropeMarker = objectiveMarker = gateMarker = beaconFlame = null;
            if (World.Expedition is not { } expedition) return;
            foreach (var terrain in World.ExpeditionTerrain)
                foreach (var cell in terrain.allPositionsWithin)
                {
                    var point = BattleGrid.Center(cell);
                    if (point.sqrMagnitude > World.Run.Config.MapRadius * World.Run.Config.MapRadius) continue;
                    terrainMarkers.Add(Shape("Terrain", worldRoot, PrimitiveType.Cube, At(point, .72f),
                        new Vector3(2.01f, 1.48f, 2.01f), stone));
                }
            clueMarker = Shape("ResearchClue", worldRoot, PrimitiveType.Cube, At(expedition.CluePosition, .8f),
                new Vector3(1.3f, 1.3f, 1.3f), blue);
            clueMarker.localRotation = Quaternion.Euler(20, 45, 0);
            surveyMarker = Shape("SurveySite", worldRoot, PrimitiveType.Cylinder, At(expedition.SurveyPosition, .65f),
                new Vector3(2.4f, .65f, 2.4f), amber);
            if (expedition.Map == ExpeditionMap.Industrial)
            {
                axleMarker = Shape("WorkshopAxle", worldRoot, PrimitiveType.Cube, At(expedition.WorkshopAxle, .4f),
                    new Vector3(1.5f, .8f, .5f), timber);
                ropeMarker = Shape("WorkshopRope", worldRoot, PrimitiveType.Cylinder, At(expedition.WorkshopRope, .2f),
                    new Vector3(1.2f, .2f, 1.2f), amber);
                objectiveMarker = Shape("Winch", worldRoot, PrimitiveType.Cylinder, At(expedition.ObjectivePosition, .55f),
                    new Vector3(1.25f, .55f, 1.25f), timber);
                if (World.ClosedWorkshopGate is { } gate)
                    gateMarker = Shape("WorkshopGate", worldRoot, PrimitiveType.Cube, At(BattleGrid.Center(gate), 1.1f),
                        new Vector3(gate.width * BattleGrid.CellSize, 2.2f, gate.height * BattleGrid.CellSize), timber);
            }
            else if (expedition.Map == ExpeditionMap.Beacon)
            {
                objectiveMarker = Shape("BeaconHearth", worldRoot, PrimitiveType.Cylinder, At(expedition.ObjectivePosition, .37f),
                    new Vector3(2.5f, .36f, 2.5f), stone);
                beaconFlame = Shape("BeaconFlame", objectiveMarker, PrimitiveType.Capsule, Vector3.up * 2,
                    new Vector3(.45f, 3, .45f), amber);
                Shape("SignalFlag", surveyMarker, PrimitiveType.Cube, new Vector3(.18f, 2f, 0),
                    new Vector3(.4f, 1.2f, .08f), timber);
            }
        }

        private void SyncExpeditionMarkers()
        {
            if (World.Expedition is not { } expedition) return;
            clueMarker.gameObject.SetActive(!expedition.FoundClue && World.Vision.IsVisible(expedition.CluePosition));
            surveyMarker.gameObject.SetActive((expedition.Map != ExpeditionMap.Industrial || expedition.BasicComplete) &&
                World.Vision.IsVisible(expedition.SurveyPosition));
            foreach (var tile in terrainMarkers)
                tile.gameObject.SetActive(World.Vision.IsExplored(new Vector2(tile.position.x, tile.position.z)));
            if (objectiveMarker != null) objectiveMarker.gameObject.SetActive(World.Vision.IsVisible(expedition.ObjectivePosition));
            if (gateMarker != null)
            {
                gateMarker.gameObject.SetActive(World.ClosedWorkshopGate.HasValue && World.Vision.IsExplored(expedition.ObjectivePosition));
                axleMarker.gameObject.SetActive(!expedition.BasicComplete && !expedition.HasAxle && World.Vision.IsVisible(expedition.WorkshopAxle));
                ropeMarker.gameObject.SetActive(!expedition.BasicComplete && !expedition.HasRope && World.Vision.IsVisible(expedition.WorkshopRope));
            }
            if (beaconFlame != null) beaconFlame.gameObject.SetActive(expedition.BeaconLit);
            if (expedition.End == ExpeditionEnd.Died) commander.gameObject.SetActive(false);
        }

        private void DrawExpeditionMapMarkers(ExpeditionState expedition)
        {
            var gold = new Color(1, .61f, .13f);
            if ((expedition.Map != ExpeditionMap.Industrial || expedition.BasicComplete) && World.Vision.IsExplored(expedition.SurveyPosition))
                Dot(expedition.SurveyPosition, gold, 7);
            if (expedition.Map == ExpeditionMap.Wilderness) return;
            if (World.Vision.IsExplored(expedition.ObjectivePosition)) Dot(expedition.ObjectivePosition, gold, 8);
            if (expedition.Map != ExpeditionMap.Industrial || expedition.BasicComplete) return;
            if (!expedition.HasAxle && World.Vision.IsExplored(expedition.WorkshopAxle)) Dot(expedition.WorkshopAxle, gold, 5);
            if (!expedition.HasRope && World.Vision.IsExplored(expedition.WorkshopRope)) Dot(expedition.WorkshopRope, gold, 5);
        }

        private void DrawExpeditionStatus()
        {
            var expedition = World.Expedition;
            var run = World.Run;
            int threat = expedition.ThreatLevel(run.Elapsed);
            string danger = $"敌情 {threat + 1}/3";
            if (expedition.BasicComplete && threat < 2)
                danger += $" · {Mathf.CeilToInt(expedition.BasicCompletedAt + (threat + 1) * 60 - run.Elapsed)} 秒后升级";
            string status = World.InteractionHint;
            if (string.IsNullOrEmpty(status) && expedition.Researching.HasValue)
                status = $"{ExpeditionState.TechName(expedition.Researching.Value)} · {Mathf.CeilToInt(expedition.ResearchLeft)} 秒";
            if (string.IsNullOrEmpty(status))
                status = World.NearShelter ? "T 打开研究" : "蓝色为线索 · 黄色为任务点";
            GUI.Label(new Rect(30, 26, 305, 30), $"{expedition.Rules.Name} · {expedition.Rules.Era}");
            GUI.Label(new Rect(30, 61, 305, 28), $"生命 {run.Health}/{expedition.MaxHealth}     物资 {run.Funds}");
            GUI.Label(new Rect(30, 92, 305, 28), World.MissionStatus);
            GUI.Label(new Rect(30, 122, 305, 28), status);
            GUI.Label(new Rect(30, 152, 305, 28), danger);
        }
    }
}
