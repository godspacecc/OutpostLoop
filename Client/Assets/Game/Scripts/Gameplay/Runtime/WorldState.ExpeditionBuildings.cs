using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class WorldState
    {
        // 建造预览只在格子、足迹或通行版本变化时重算连通性。
        private RectInt previewExpeditionFootprint;
        private Vector2Int previewExpeditionOrigin;
        private int previewExpeditionRevision = -1;
        private bool previewExpeditionAccess;
        private int[] previewHaulingField;
        // 同时只有一座采运棚，站外距离场在占地改变时更新。
        private int haulingRevision = -1;
        private int[] haulingField;

        public string BuildingRequirement(ExpansionKind kind)
        {
            if (Expedition == null) return "";
            if (kind == ExpansionKind.Extractor && !Expedition.Has(ExpeditionTech.Handcart)) return "需要手推车";
            if ((kind == ExpansionKind.Barricade || kind == ExpansionKind.Turret) &&
                !Expedition.Has(ExpeditionTech.Fortifications)) return "需要简易工事";
            return "";
        }

        private PlacementIssue PreviewExpeditionPlacement(ExpansionKind kind, RectInt footprint, int moving,
            ExpansionSpec spec, out int node)
        {
            node = -1;
            int limit = kind switch
            {
                ExpansionKind.Shelter or ExpansionKind.Extractor => 1,
                ExpansionKind.Turret => 2,
                ExpansionKind.Barricade => 6,
                _ => 0
            };
            if (moving >= 0 || limit == 0) return PlacementIssue.Inactive;
            if (BuildingRequirement(kind).Length > 0) return PlacementIssue.MissingResearch;
            int count = 0;
            foreach (var site in Buildings)
                if (site.Kind == kind) count++;
            if (count >= limit) return PlacementIssue.Limit;
            if (!InBuildArea(footprint, CommanderPosition)) return PlacementIssue.Outside;
            if (!Vision.Covers(footprint)) return PlacementIssue.NotVisible;
            if (!TerrainAllows(footprint) || UnitOccupies(footprint) || MineralOccupies(footprint))
                return PlacementIssue.Occupied;
            if (!Run.CanAfford(spec.Cost)) return PlacementIssue.InsufficientStock;

            var origin = BattleGrid.Cell(CommanderPosition);
            if (previewExpeditionRevision != navigation.Revision || !previewExpeditionFootprint.Equals(footprint) ||
                previewExpeditionOrigin != origin)
            {
                previewExpeditionRevision = navigation.Revision;
                previewExpeditionFootprint = footprint;
                previewExpeditionOrigin = origin;
                previewHaulingField = null;
                var field = navigation.Field(new RectInt(origin, Vector2Int.one), 0, footprint);
                previewExpeditionAccess = CanReachProtectedPositions(field) && CanReachExpeditionSite(field, footprint);
            }
            if (!previewExpeditionAccess) return PlacementIssue.BlocksAccess;
            if (kind != ExpansionKind.Extractor) return PlacementIssue.None;
            previewHaulingField ??= navigation.Field(footprint, 1.1f, footprint);
            node = FindHaulingNode(footprint, previewHaulingField);
            return node >= 0 ? PlacementIssue.None : PlacementIssue.NoResource;
        }

        private bool CanReachExpeditionSite(int[] field, RectInt footprint)
        {
            var ring = new RectInt(footprint.min - Vector2Int.one, footprint.size + Vector2Int.one * 2);
            foreach (var cell in ring.allPositionsWithin)
                if (navigation.Distance(field, cell) >= 0) return true;
            return false;
        }

        private int FindHaulingNode(RectInt footprint, int[] field)
        {
            int best = -1;
            float closest = 100;
            var origin = BattleGrid.Center(footprint);
            for (int index = 0; index < Nodes.Count; index++)
            {
                var node = Nodes[index];
                float distance = (node.Position - origin).sqrMagnitude;
                if (node.Remaining <= 0 || node.ObservedRemaining < 0 || distance > closest ||
                    navigation.Distance(field, BattleGrid.Cell(node.Position)) < 0) continue;
                if (distance == closest && best >= 0) continue;
                best = index;
                closest = distance;
            }
            return best;
        }

        /// <summary>从实际绑定的共享资源点扣除库存，断路期间停采且不积累采运时长。</summary>
        private void StepExpeditionHauling(ExpansionSite site, float dt)
        {
            if (haulingRevision != navigation.Revision)
            {
                haulingRevision = navigation.Revision;
                haulingField = navigation.Field(site.Footprint, 1.1f);
            }
            if (site.NodeIndex < 0 || Nodes[site.NodeIndex].Remaining == 0)
            {
                site.NodeIndex = FindHaulingNode(site.Footprint, haulingField);
                site.WorkClock = 0;
            }
            if (site.NodeIndex < 0) return;
            var node = Nodes[site.NodeIndex];
            if (navigation.Distance(haulingField, BattleGrid.Cell(node.Position)) < 0) return;
            site.WorkClock += (Expedition.Has(ExpeditionTech.Pulley) ? 4 : 2) * dt;
            int amount = Mathf.Min(node.Remaining, Mathf.FloorToInt(site.WorkClock + .00001f));
            site.WorkClock -= amount;
            node.Remaining -= amount;
            Run.CreditDelivery(amount);
        }

        /// <summary>玩家二次确认后移除探索拒马，废料不退回本局库存。</summary>
        public bool DismantleExpeditionBarricade(int index)
        {
            if (!Active || Expedition == null || index < 0 || index >= Buildings.Count ||
                Buildings[index].Kind != ExpansionKind.Barricade) return false;
            Buildings[index].Damage(int.MaxValue);
            RefreshNavigation();
            return true;
        }
    }
}
