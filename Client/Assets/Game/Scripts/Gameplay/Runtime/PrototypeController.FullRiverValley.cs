using System.Collections.Generic;
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
        /// <summary>把领域平面位置投到当前地图真实地面或桥面高度。</summary>
        private Vector3 SurvivalPosition(Vector2 point, float offset) => At(point, (survivalWorldGeometry?.Height(point) ?? 0) + offset);
        private Vector3 ScavengePlayerPosition(float offset) =>
            SurvivalPosition(scavengeRun.Player, offset + scavengeRun.PlayerHeightOffset);

        /// <summary>保持原物资总量，整图版仅重排到已确认的区域与容器位置。</summary>
        private ScavengeSurvival CreateSurvivalState(int seed)
        {
            var state = ScavengeSurvival.Create(seed, survivalLayoutVersion >= 1);
            state.layoutVersion = survivalLayoutVersion;
            if (survivalWorldGeometry != null)
                for (int i = 0; i < state.caches.Length; i++) state.caches[i].point = survivalWorldGeometry.Caches[i];
            return state;
        }

        /// <summary>旧档结束后换种子出发进入整图；同种子重试仍可保留旧地图。</summary>
        private void StartFreshSurvivalFromResult()
        {
            if (!CloseSurvivalSave()) return;
            if (survivalLayoutVersion >= RiverValleyResidence.Version) { RestartScavenge(); return; }
            LeaveScavenge();
            CrossToSurvival();
        }

        /// <summary>用实际整图替代嵌入小街区；六处室内与模型、领域墙体共用矩形。</summary>
        private IEnumerable<(float value, string label)> CreateFullRiverValleyMap(Material floor, Material walls, Material roof)
        {
            var content = Game.ResourceLoader.LoadAsset<RiverValleyGameplayContent>(Game.GameplayArtAddress.RiverValley);
            if (content?.layout == null || content.surface == null) throw new System.InvalidOperationException("河谷镇整图引用缺失。");
            survivalCityArt = RiverValleyCityArtCatalog.Load();
            var town = RiverValleyTownPreview.CreateWorld(scavengeRoot, content.layout, content.surface,
                catalog: survivalCityArt, deferBuild: true, registerBuildingShell: parts => RegisterSurvivalShell(-1, parts),
                registerSightBox: (center, rotation, size) => survivalSight.AddBox(center, rotation, size));
            foreach (var step in town.BuildStages()) yield return step;
            yield return (.86f, "正在布置可进入的建筑");
            survivalWorldGeometry = town.WorldGeometry;
            if(survivalLayoutVersion>=RiverValleyShopInterior.Version)survivalWorldGeometry.EnableShopInterior();
            if (survivalLayoutVersion >= RiverValleyResidence.UpperFloorVersion)
                survivalWorldGeometry.EnableResidence(survivalLayoutVersion);
            currentSurvivalRooms = new SurvivalRoomSet(new[] { survivalWorldGeometry.Interiors[0], survivalWorldGeometry.Interiors[1] });
            riverValleyRegion = JsonUtility.FromJson<RiverValleyRegion>(content.layout.text);
            var wood = ScavengeMaterial(new Color(.43f,.32f,.23f));
            for (int room = 0; room < 2; room++) CreateSurvivalRoom(room, walls, floor, wood, roof);
            if(survivalWorldGeometry.Shop!=null)CreateMainStreetShopInterior();
            var allRooms = new SurvivalRoomSet(survivalWorldGeometry.Interiors);
            for (int i = 2; i < 6; i++)
            {
                var area = allRooms.Footprint(i);
                var interior = RegisterSurvivalInterior(i, area);
                ScavengeBox("重点建筑室内地面", SurvivalPosition(area.center,.035f), new Vector3(area.width,.065f,area.height),floor,interior);
                if (i == 4 && survivalCityArt != null)
                {
                    var sign = ScavengeBox("医院标志",SurvivalPosition(new Vector2(area.xMin-.25f,area.center.y),2.45f),new Vector3(3.34f,.66f,.14f),walls);
                    sign.rotation=Quaternion.Euler(0,90,0);
                    RegisterSurvivalShell(i, sign.GetComponentsInChildren<Renderer>());
                }
                foreach (var wall in allRooms.Walls(i))
                {
                    var section = ScavengeBox("重点建筑入口墙", SurvivalPosition(wall.center,1.42f),
                        new Vector3(wall.width,2.84f,wall.height),walls, sightBox: true);
                    RegisterSurvivalShell(i, section.GetComponentsInChildren<Renderer>());
                    survivalWorldGeometry.Solids.Add(new RiverValleyWorldGeometry.Solid(wall.center,wall.size,0));
                }
                var cover = ScavengeBox("重点建筑屋顶",SurvivalPosition(area.center,2.92f),new Vector3(area.width,.16f,area.height),roof, sightBox: true);
                RegisterSurvivalShell(i, cover.GetComponentsInChildren<Renderer>(), roof: true);
                // 保留入口至中央搜点的通道，少量设施让大房间有可辨认的尺度。
                for (int side = -1; side <= 1; side += 2)
                {
                    var point = new Vector2(area.xMin + 6, area.center.y + side * Mathf.Min(5,area.height / 3));
                    var size = i == 3 ? new Vector3(3,1.5f,2) : new Vector3(2.1f,.75f,1.1f);
                    var prop = ScavengeBox(i == 3 ? "仓库货架" : "室内服务设施",SurvivalPosition(point,size.y/2),size,i==3?wood:floor,interior);
                    // 货架正面朝向探索镜头一侧，避免只看见背板。
                    prop.rotation=Quaternion.Euler(0,180,0);
                    if (survivalCityArt != null) RiverValleyShelfDressing.Fill(prop,survivalCityArt,i==3);
                    survivalWorldGeometry.Solids.Add(new RiverValleyWorldGeometry.Solid(point,new Vector2(size.x,size.z),0));
                    RegisterScavengeOccluder(prop.GetComponentsInChildren<Renderer>());
                }
            }
            survivalWorldGeometry.Finish();
            // 暂用项目现有人形和待机动作表现诊所幸存者。
            var survivor = Instantiate(LoadScavengeHuman(), survivalInteriors[2].Root, false);
            survivalClinicActor = survivor.transform;
            survivor.name = "诊所幸存者";
            survivor.transform.SetPositionAndRotation(SurvivalPosition(survivalWorldGeometry.ClinicSurvivor, 0),
                Quaternion.LookRotation(Vector3.left));
            survivor.GetComponentInChildren<Animator>().applyRootMotion = false;
            // 仅临时区分同一底模的两名角色，不写回共用材质。
            var survivorTint = new MaterialPropertyBlock();
            survivorTint.SetColor("_Color", new Color(.27f, .67f, .76f));
            foreach (var renderer in survivor.GetComponentsInChildren<SkinnedMeshRenderer>())
                renderer.SetPropertyBlock(survivorTint);
        }
    }
}
