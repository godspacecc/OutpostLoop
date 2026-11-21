using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        private const float ScavengeCameraFieldOfView = 44f;
        private const float ScavengeCameraDistance = 18.5f;
        // 俯角控制纵深，偏航让街道和仓库立面同时可见。
        private static readonly Quaternion ScavengeViewRotation = Quaternion.Euler(43f, -20f, 0f);
        private static readonly Quaternion ScavengeGroundRotation = Quaternion.Euler(0f, -20f, 0f);
        private readonly List<Renderer[]> scavengeOccluders = new List<Renderer[]>();
        // 镜头避挡也照顾周围可走空间，不依赖鼠标朝向。
        private readonly OcclusionRay[] survivalCameraContext = new OcclusionRay[10];
        private Vector3 scavengeCameraFocus;

        /// <summary>以画面朝向解释 WASD，保留原移动速度和地形判定。</summary>
        private static Vector2 ScavengeWorldMovement(Vector2 input)
        {
            var world = ScavengeGroundRotation * At(input, 0);
            return new Vector2(world.x, world.z);
        }

        /// <summary>近距离跟随玩家，给前方多留一小段可观察空间。</summary>
        private void UpdateScavengeCamera(bool snap)
        {
            var lookAhead = scavengeRun.Driving
                ? Quaternion.Euler(0, scavengeRun.VehicleHeading, 0) * Vector3.forward * 3
                : ScavengeGroundRotation * Vector3.forward * 1.5f;
            var desired = ScavengePlayerPosition(0) + lookAhead;
            scavengeCameraFocus = snap ? desired : Vector3.Lerp(scavengeCameraFocus, desired,
                1 - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            float distance = Mathf.Max(ScavengeCameraDistance, 25f / gameCamera.aspect);
            gameCamera.transform.SetPositionAndRotation(scavengeCameraFocus - ScavengeViewRotation * Vector3.forward * distance,
                ScavengeViewRotation);
            UpdateScavengeOccluders();
        }

        /// <summary>同一墙段或屋顶的部件一起切除，逻辑地形仍由探索规则控制。</summary>
        private void RegisterScavengeOccluder(params Renderer[] renderers) => scavengeOccluders.Add(renderers);

        private void UpdateScavengeOccluders()
        {
            var origin = gameCamera.transform.position;
            var ground = ScavengePlayerPosition(0);
            var torso = new OcclusionRay(origin, ground + Vector3.up * .95f);
            var feet = new OcclusionRay(origin, ground + Vector3.up * .22f);
            int cache = scavengeRun.OpenCache >= 0 ? scavengeRun.OpenCache : scavengeRun.NearbyCache;
            // 搜点高度可能遍历整张地图的道路；每帧只查询一次。
            var cacheRay = cache >= 0 ? new OcclusionRay(origin, SurvivalPosition(scavengeRun.CachePoints[cache], .36f)) : default;
            if (survivalSight != null)
            {
                var side = gameCamera.transform.right * 2.2f;
                var forward = Vector3.Cross(side.normalized, Vector3.up) * 1.8f;
                survivalCameraContext[0] = torso;
                for (int x = 0; x < 3; x++)
                for (int z = 0; z < 3; z++)
                {
                    var target = ground + side * (x - 1) + forward * (z - 1) + Vector3.up * .22f;
                    var point = new Vector2(target.x, target.z);
                    bool sameSide = scavengeRun.PlayerOnGround ? scavengeRun.ClearRunLine(scavengeRun.Player, point) :
                        survivalWorldGeometry?.Residence?.ContainsShot(point, scavengeRun.PlayerSurface) == true;
                    survivalCameraContext[1 + x * 3 + z] = sameSide || (x == 1 && z == 1)
                        ? new OcclusionRay(origin, target) : default;
                }
                UpdateSurvivalShells(survivalCameraContext);
            }
            foreach (var segment in scavengeOccluders)
            {
                bool hidden = false;
                foreach (var part in segment)
                {
                    if (part == null || !part.enabled || !part.gameObject.activeInHierarchy) continue;
                    var bounds = part.bounds;
                    hidden = torso.BlockedBy(bounds) || feet.BlockedBy(bounds) ||
                        (cache >= 0 && cacheRay.BlockedBy(bounds));
                    if (hidden) break;
                }
                foreach (var part in segment)
                    if (part != null) part.forceRenderingOff = hidden;
            }
        }

        /// <summary>同一观察目标的射线与距离在整轮遮挡扫描中复用。</summary>
        private readonly struct OcclusionRay
        {
            private readonly Ray ray;
            private readonly float maxDistance;

            internal OcclusionRay(Vector3 origin, Vector3 target)
            {
                var delta = target - origin;
                ray = new Ray(origin, delta);
                maxDistance = delta.magnitude - .06f;
            }

            internal bool BlockedBy(Bounds bounds) => maxDistance > 0 && bounds.IntersectRay(ray, out float hit) && hit < maxDistance;

            /// <summary>先查世界包围盒，再用渲染器本地范围排除旋转建筑的空角。</summary>
            internal bool BlockedBy(Renderer part)
            {
                if (!BlockedBy(part.bounds)) return false;
                var inverse = part.worldToLocalMatrix;
                var direction = inverse.MultiplyVector(ray.direction);
                var local = new Ray(inverse.MultiplyPoint3x4(ray.origin), direction);
                return part.localBounds.IntersectRay(local, out float hit) && hit < maxDistance * direction.magnitude;
            }
        }
    }
}
