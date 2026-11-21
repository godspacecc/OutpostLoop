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
            if (cache >= 0 && !CanSeeSurvivalCache(cache)) cache = -1;
            // 搜点高度可能遍历整张地图的道路；每帧只查询一次。
            var cacheRay = cache >= 0 ? new OcclusionRay(origin, SurvivalPosition(scavengeRun.CachePoints[cache], .36f)) : default;
            UpdateSurvivalShells(torso, feet, cacheRay, cache >= 0);
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
                    if (part != null) part.forceRenderingOff = hidden || survivalSightHiddenProps.Contains(part);
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

            internal bool BlockedBy(Bounds bounds) => bounds.IntersectRay(ray, out float hit) && hit < maxDistance;

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
