using Game;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 布局和视口变化时统一刷新，绘制与输入读同一组矩形。
        private readonly Rect[] perceptionRects = new Rect[PerceptionGeometry.Count];
        private PerceptionLayout lastPerceptionLayout;
        private Rect lastPerceptionArea;

        internal Camera PerceptionPreviewCamera => RiverValleyActionActive ? gameCamera : null;

        private PerceptionLayout CurrentPerception =>
            Game.Game.Interface.GetSystem<SettingsSystem>().Perception;

        private Rect PerceptionRect(PerceptionGroup group)
        {
            var layout = CurrentPerception;
            var area = PerceptionGeometry.ScreenArea;
            if (lastPerceptionLayout != layout || lastPerceptionArea != area)
            {
                PerceptionGeometry.Resolve(layout, area, perceptionRects);
                lastPerceptionLayout = layout;
                lastPerceptionArea = area;
            }
            return perceptionRects[(int)group];
        }

        private bool PerceptionVisible(PerceptionGroup group) => CurrentPerception.Get(group).Visible == true;

        private bool ShowPerceptionGroup(PerceptionGroup group, Rect bounds)
        {
            float width = Screen.width / HudScale, height = Screen.height / HudScale;
            return PerceptionVisible(group) &&
                (!SurvivalInventoryOpen && !survivalBuildOpen || !bounds.Overlaps(SurvivalInventory(width, height))) &&
                (!ShowSurvivalBuild || !bounds.Overlaps(SurvivalBuildRect(width, height)));
        }

        private bool ShowPerceptionMap()
        {
            if (!CanShowSurvivalHud() || !PerceptionVisible(PerceptionGroup.Map) ||
                (survivalPackOpen && inventoryRoot)) return false;
            var bounds = PerceptionRect(PerceptionGroup.Map);
            return ShowPerceptionGroup(PerceptionGroup.Map, bounds);
        }
    }
}
