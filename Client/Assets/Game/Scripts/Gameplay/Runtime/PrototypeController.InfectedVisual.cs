using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {

        private RiverValleyInfectedVisual[] infectedVisuals;

        /// <summary>只在当前河谷镇整图接入已确认的感染者表现。</summary>
        private bool TryCreateRiverValleyInfected()
        {
            // 公开检出时由本地缓存恢复原包依赖；缺包时沿用原有占位表现。
            if (Game.ResourceLoader.Current is Game.YooAssetResourceLoader loader &&
                !System.Linq.Enumerable.All(Game.GameplayArtAddress.InfectedOptional, loader.HasAsset)) return false;
            var idle = LoadInfected<AnimationClip>("Z_Idle");
            var walk = LoadInfected<AnimationClip>("Z_Walk_InPlace");
            var run = LoadInfected<AnimationClip>("Z_Run_InPlace");
            var ordinary = LoadInfected<GameObject>("Zombie2");
            var fast = LoadInfected<GameObject>("Zombie3");
            if (idle == null || walk == null || run == null || ordinary == null || fast == null) return false;
            infectedVisuals = new RiverValleyInfectedVisual[scavengeEnemies.Length];
            var normalMaterial = CreateInfectedMaterial(ordinary, false);
            var fastMaterial = CreateInfectedMaterial(fast, true);
            for (int i = 0; i < infectedVisuals.Length; i++)
            {
                bool quick = ScavengeTown.IsFastPatrol(i);
                var actor = Instantiate(quick ? fast : ordinary, scavengeRoot, false);
                actor.name = (quick ? "快速感染者 " : "普通感染者 ") + i;
                scavengeEnemies[i] = actor.transform;
                var visual = actor.AddComponent<RiverValleyInfectedVisual>();
                visual.Initialize(quick, survivalWorldGeometry.Height, quick ? fastMaterial : normalMaterial, idle, walk, run);
                infectedVisuals[i] = visual;
            }
            return true;
        }

        private Material CreateInfectedMaterial(GameObject prefab, bool fast)
        {
            var source = prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            var material = new Material(LoadInfected<Shader>("InfectedMatte"));
            material.mainTexture = source.mainTexture;
            material.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
            material.SetFloat("_Contrast", fast ? 1f : 0f);
            scavengeMaterials.Add(material);
            return material;
        }

        private static T LoadInfected<T>(string name) where T : Object =>
            Game.ResourceLoader.LoadAsset<T>(Game.GameplayArtAddress.Infected + name);
    }
}
