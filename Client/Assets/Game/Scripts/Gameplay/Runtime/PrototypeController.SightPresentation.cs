using UnityEngine;
using OutpostLoop.RiverValley;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 相机先绘制完整场景，再使用角色当前视野更新和合成最后所见。
        private RiverValleyFog survivalFog;
        private Texture2D survivalSightMask;
        private Transform survivalClinicActor;

        /// <summary>开局和重试分别开启新的观察记录，续玩沿用档内世界地块。</summary>
        private void StartSurvivalFog(RiverValleyFogPageSave[] saved)
        {
            ClearSurvivalFog();
            survivalSightMask = new Texture2D(RiverValleySight.RayCount, 1, TextureFormat.RFloat, false, true)
            {
                name = "角色视野距离",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            survivalFog = gameCamera.gameObject.AddComponent<RiverValleyFog>();
            if (survivalFog.Initialize(saved)) return;
            Destroy(survivalFog);
            survivalFog = null;
        }

        /// <summary>家具进入相机底图，幸存者仍依照实际视线显隐。</summary>
        private void SyncSurvivalSightPresentation()
        {
            if (survivalClinicActor)
                survivalClinicActor.gameObject.SetActive(CanSeeSurvivalInteriorAt(survivalWorldGeometry.ClinicSurvivor));
            if (!survivalFog) return;
            survivalSightMask.SetPixelData(survivalSight.Distances, 0);
            survivalSightMask.Apply(false);
            var surface = scavengeRun.PlayerSurface;
            float height = surface == ResidenceSurface.Ground ? .05f : SurvivalPosition(survivalWorldGeometry.Residence.Footprint.center,
                surface == ResidenceSurface.Roof || surface == ResidenceSurface.RoofStairs
                    ? RiverValleyResidence.RoofHeight : RiverValleyResidence.StoreyHeight).y;
            survivalFog.SetView(new Vector3(survivalSight.Eye.x, survivalSight.FeetHeight, survivalSight.Eye.z),
                surface, height, survivalSightMask);
        }

        /// <summary>归还镜头状态。每趟行动的记忆有各自独立的渲染资源。</summary>
        private void ClearSurvivalFog()
        {
            if (survivalFog)
            {
                survivalFog.ReleaseView();
                survivalFog.enabled = false;
                Destroy(survivalFog);
                survivalFog = null;
            }
            if (survivalSightMask) Destroy(survivalSightMask);
            survivalSightMask = null;
        }
    }
}
