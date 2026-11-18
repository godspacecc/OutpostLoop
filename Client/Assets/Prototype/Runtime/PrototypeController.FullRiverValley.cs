using UnityEngine;
using OutpostLoop.RiverValley;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 版本只在开始／恢复行动时选择，绝不把旧存档坐标直接映射到整图。
        private int survivalLayoutVersion;
        // 纯几何快照与本趟房间集合，离开行动时一起释放引用。
        private RiverValleyWorldGeometry survivalWorldGeometry;
        private SurvivalRoomSet currentSurvivalRooms;
        private SurvivalRoomSet CurrentSurvivalRooms => currentSurvivalRooms ?? (currentSurvivalRooms = new SurvivalRoomSet());
        // 与六处重点入口按同一顺序展示名称。
        private static readonly string[] FullTownInteriorNames = { "主街杂货店", "住宅", "社区诊所", "仓库", "医院入口层", "西岸桥头店铺" };
        /// <summary>把领域平面位置投到当前地图真实地面或桥面高度。</summary>
        private Vector3 SurvivalPosition(Vector2 point, float offset) => At(point, (survivalWorldGeometry?.Height(point) ?? 0) + offset);

        /// <summary>保持原物资总量，整图版仅重排到已确认的区域与容器位置。</summary>
        private ScavengeSurvival CreateSurvivalState(int seed)
        {
            var state = ScavengeSurvival.Create(seed, survivalLayoutVersion == 1);
            state.layoutVersion = survivalLayoutVersion;
            if (survivalWorldGeometry != null)
                for (int i = 0; i < state.caches.Length; i++) state.caches[i].point = survivalWorldGeometry.Caches[i];
            return state;
        }

        /// <summary>旧档结束后换种子出发进入整图；同种子重试仍可保留旧地图。</summary>
        private void StartFreshSurvivalFromResult()
        {
            if (!CloseSurvivalSave()) return;
            if (survivalLayoutVersion == 1) { RestartScavenge(); return; }
            LeaveScavenge();
            CrossToWorld(StartSurvival);
        }

        /// <summary>用实际整图替代嵌入小街区；六处室内与模型、领域墙体共用矩形。</summary>
        private void CreateFullRiverValleyMap(Material soil, Material floor, Material walls, Material roof)
        {
            var content = UnityEngine.Resources.Load<RiverValleyGameplayContent>("Prototype/RiverValleyGameplayContent");
            if (content?.layout == null || content.surface == null) throw new System.InvalidOperationException("河谷镇整图引用缺失。");
            survivalCityArt = RiverValleyCityArtCatalog.Load();
            var town = RiverValleyTownPreview.CreateWorld(scavengeRoot, content.layout, content.surface);
            survivalWorldGeometry = town.WorldGeometry;
            currentSurvivalRooms = new SurvivalRoomSet(new[] { survivalWorldGeometry.Interiors[0], survivalWorldGeometry.Interiors[1] });
            riverValleyRegion = JsonUtility.FromJson<RiverValleyRegion>(content.layout.text);
            var wood = ScavengeMaterial(new Color(.43f,.32f,.23f));
            for (int room = 0; room < 2; room++) CreateSurvivalRoom(room, walls, floor, wood, roof);
            var allRooms = new SurvivalRoomSet(survivalWorldGeometry.Interiors);
            for (int i = 2; i < 6; i++)
            {
                var area = allRooms.Footprint(i);
                ScavengeBox("重点建筑室内地面", SurvivalPosition(area.center,.035f), new Vector3(area.width,.065f,area.height),floor);
                if (i == 4 && survivalCityArt != null)
                {
                    var sign = ScavengeBox("医院标志",SurvivalPosition(new Vector2(area.xMin-.25f,area.center.y),2.45f),new Vector3(3.34f,.66f,.14f),walls);
                    sign.rotation=Quaternion.Euler(0,90,0);
                    RegisterScavengeOccluder(sign.GetComponent<Renderer>());
                }
                var parts = new System.Collections.Generic.List<Renderer>();
                foreach (var wall in allRooms.Walls(i))
                {
                    parts.Add(ScavengeBox("重点建筑入口墙", SurvivalPosition(wall.center,1.42f), new Vector3(wall.width,2.84f,wall.height),walls).GetComponent<Renderer>());
                    survivalWorldGeometry.Solids.Add(new RiverValleyWorldGeometry.Solid(wall.center,wall.size,0));
                }
                parts.Add(ScavengeBox("重点建筑屋顶",SurvivalPosition(area.center,2.92f),new Vector3(area.width,.16f,area.height),roof).GetComponent<Renderer>());
                foreach (var part in parts) RegisterScavengeOccluder(part);
                // 保留入口至中央搜点的通道，少量设施让大房间有可辨认的尺度。
                for (int side = -1; side <= 1; side += 2)
                {
                    var point = new Vector2(area.xMin + 6, area.center.y + side * Mathf.Min(5,area.height / 3));
                    var size = i == 3 ? new Vector3(3,1.5f,2) : new Vector3(2.1f,.75f,1.1f);
                    var prop = ScavengeBox(i == 3 ? "仓库货架" : "室内服务设施",SurvivalPosition(point,size.y/2),size,i==3?wood:floor);
                    survivalWorldGeometry.Solids.Add(new RiverValleyWorldGeometry.Solid(point,new Vector2(size.x,size.z),0));
                    RegisterScavengeOccluder(prop.GetComponent<Renderer>());
                }
            }
            survivalWorldGeometry.Finish();
            foreach (Transform child in town.transform)
                if (child.name == "封闭建筑") RegisterScavengeOccluder(child.GetComponentsInChildren<Renderer>());
        }

        /// <summary>重点入口有现场名称；无入口标记的其余建筑保持封闭。</summary>
        private void DrawFullRiverValleyEntrances()
        {
            if (survivalWorldGeometry == null) return;
            var rooms = new SurvivalRoomSet(survivalWorldGeometry.Interiors);
            for (int i = 0; i < 6; i++)
            {
                var point = rooms.Door(i).center;
                if (Vector2.Distance(point, scavengeRun.Player) > 45) continue;
                var screen = gameCamera.WorldToScreenPoint(SurvivalPosition(point, 3.4f));
                if (screen.z <= 0) continue;
                var pin = new Vector2(screen.x, Screen.height - screen.y) / HudScale;
                if (pin.y < 165 || pin.y > Screen.height / HudScale - 120) continue;
                GUI.Box(new Rect(pin.x - 90, pin.y - 15, 180, 30), FullTownInteriorNames[i] + " · 入口");
            }
        }
    }
}
