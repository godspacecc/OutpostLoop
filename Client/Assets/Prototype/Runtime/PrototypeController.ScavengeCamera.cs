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
            var lookAhead = ScavengeGroundRotation * Vector3.forward * 1.5f;
            var desired = At(scavengeRun.Player, 0) + lookAhead;
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
            var torso = At(scavengeRun.Player, .95f);
            var feet = At(scavengeRun.Player, .22f);
            int cache = scavengeRun.OpenCache >= 0 ? scavengeRun.OpenCache : scavengeRun.NearbyCache;
            foreach (var segment in scavengeOccluders)
            {
                bool hidden = false;
                foreach (var part in segment)
                {
                    if (part == null || !part.enabled || !part.gameObject.activeInHierarchy) continue;
                    var bounds = part.bounds;
                    hidden = BlocksView(bounds, origin, torso) || BlocksView(bounds, origin, feet) ||
                        (cache >= 0 && BlocksView(bounds, origin, At(ScavengeLayout.Caches[cache], .36f)));
                    if (hidden) break;
                }
                foreach (var part in segment)
                    if (part != null) part.forceRenderingOff = hidden;
            }
        }

        private static bool BlocksView(Bounds bounds, Vector3 origin, Vector3 target)
        {
            var toTarget = target - origin;
            float distance = toTarget.magnitude;
            return bounds.IntersectRay(new Ray(origin, toTarget), out float hit) && hit < distance - .06f;
        }
    }
}
