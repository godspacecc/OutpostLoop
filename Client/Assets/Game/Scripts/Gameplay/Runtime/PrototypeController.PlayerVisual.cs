using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        /// <summary>采用用户选定的 Prototype 人形，脚底坐标与领域位置一致。</summary>
        private Transform CreateScavengePlayer()
        {
            var prefab = Game.ResourceLoader.LoadAsset<GameObject>(Game.GameplayArtAddress.Player);
            if (prefab == null) throw new System.InvalidOperationException("缺少已选定的 PrototypePlayer 资源");
            var player = Instantiate(prefab, scavengeRoot, false);
            player.name = "探索角色_POLYGON_Prototype";
            var visual = player.AddComponent<PrototypePlayerVisual>();
            visual.Initialize(
                () => scavengePaused || UtilityWindowOpen || survivalConfirmNew ||
                    survivalHelpOpen || survivalConfirmExit || survivalRegionMapOpen ||
                    scavengeRun == null || scavengeRun.Outcome != ScavengeOutcome.Exploring,
                () => scavengeRun != null && scavengeRun.SurvivalSprint);
            return player.transform;
        }
    }
}
