using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        /// <summary>共用已选定的探索人形资源，按当前角色职责组装表现。</summary>
        private static GameObject LoadScavengeHuman()
        {
            var prefab = Game.ResourceLoader.LoadAsset<GameObject>(Game.GameplayArtAddress.Player);
            if (prefab == null) throw new System.InvalidOperationException("缺少已选定的 PrototypePlayer 资源");
            return prefab;
        }

        /// <summary>采用用户选定的 Prototype 人形，脚底坐标与领域位置一致。</summary>
        private Transform CreateScavengePlayer()
        {
            var player = Instantiate(LoadScavengeHuman(), scavengeRoot, false);
            player.name = "探索角色_POLYGON_Prototype";
            var visual = player.AddComponent<PrototypePlayerVisual>();
            visual.Initialize(
                () => scavengePaused || UtilityWindowOpen ||
                    survivalHelpOpen || survivalConfirmExit || survivalRegionMapOpen ||
                    scavengeRun == null || scavengeRun.Outcome != ScavengeOutcome.Exploring,
                () => scavengeRun != null && scavengeRun.SurvivalSprint);
            player.AddComponent<PrototypeCombatVisual>().Initialize(
                () => scavengeRun,
                () => scavengePaused || UtilityWindowOpen || survivalHelpOpen || survivalConfirmExit || survivalRegionMapOpen ||
                    scavengeRun == null || scavengeRun.Outcome != ScavengeOutcome.Exploring);
            return player.transform;
        }
    }
}
