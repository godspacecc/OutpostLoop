using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 蓝色传送门原件为可选本机资源，缺少任一阶段时使用撤离点文字提示。
        private const string RiverValleyPortalPath = "RiverValleyPortal/PortalBlue";
        private const float RiverValleyOpenPortalScale = 1.45f;
        // 按展开后可见椭圆的半高抬起，微光与开合特效共用同一个中心。
        private const float RiverValleyPortalCenterHeight = 2.25f;
        private GameObject riverValleyPortalIdle, riverValleyPortalOpen, riverValleyPortalClose;
        private GameObject riverValleyPortalInstance;
        private int riverValleyPortalStage;
        private float riverValleyPortalStageEnd;

        /// <summary>在本局真实撤离点布置缩小的微光，旧存档也沿用自己的撤离坐标。</summary>
        private void ResetRiverValleyPortal()
        {
            ClearRiverValleyPortalEffect();
            riverValleyPortalStage = 0;
            riverValleyPortalIdle = UnityEngine.Resources.Load<GameObject>(RiverValleyPortalPath + "Idle");
            riverValleyPortalOpen = UnityEngine.Resources.Load<GameObject>(RiverValleyPortalPath + "Open");
            riverValleyPortalClose = UnityEngine.Resources.Load<GameObject>(RiverValleyPortalPath + "Close");
            if (riverValleyPortalIdle && riverValleyPortalOpen && riverValleyPortalClose)
                ShowRiverValleyPortalEffect(riverValleyPortalIdle, .34f, "撤离点微光", false);
        }

        /// <summary>读取既有结算结果，在成功撤离后播放一次开合，再恢复原结算画面。</summary>
        private bool PresentRiverValleyPortal()
        {
            if (riverValleyPortalStage == 0)
            {
                if (!riverValleyPortalInstance || scavengeRun.Outcome == ScavengeOutcome.Exploring) return false;
                if (scavengeRun.Outcome != ScavengeOutcome.Extracted)
                {
                    ClearRiverValleyPortalEffect();
                    return false;
                }
                riverValleyPortalStage = 1;
                riverValleyPortalStageEnd = Time.unscaledTime + .9f;
                ShowRiverValleyPortalEffect(riverValleyPortalOpen, RiverValleyOpenPortalScale, "撤离传送门开启", true);
            }
            else if (riverValleyPortalStage == 1 && Time.unscaledTime >= riverValleyPortalStageEnd)
            {
                riverValleyPortalStage = 2;
                riverValleyPortalStageEnd = Time.unscaledTime + .85f;
                ShowRiverValleyPortalEffect(riverValleyPortalClose, RiverValleyOpenPortalScale, "撤离传送门关闭", true);
            }
            else if (riverValleyPortalStage == 2 && Time.unscaledTime >= riverValleyPortalStageEnd)
            {
                riverValleyPortalStage = 3;
                ClearRiverValleyPortalEffect();
            }
            return RiverValleyPortalPlaying;
        }

        // 播放期间保留当前俯视镜头，避免结算面板挡住撤离点。
        private bool RiverValleyPortalPlaying => riverValleyPortalStage == 1 || riverValleyPortalStage == 2;

        /// <summary>实例跟随当前场景生命周期，使用与结算过渡相同的时间基准。</summary>
        private void ShowRiverValleyPortalEffect(GameObject prefab, float scale, string label, bool enableLights)
        {
            ClearRiverValleyPortalEffect();
            riverValleyPortalInstance = Instantiate(prefab, scavengeRoot, false);
            riverValleyPortalInstance.name = label;
            var effect = riverValleyPortalInstance.transform;
            effect.position = SurvivalPosition(scavengeRun.ExitPoint, RiverValleyPortalCenterHeight);
            effect.localScale *= scale;
            foreach (var particle in riverValleyPortalInstance.GetComponentsInChildren<ParticleSystem>())
            {
                var main = particle.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.useUnscaledTime = true;
            }
            foreach (var animator in riverValleyPortalInstance.GetComponentsInChildren<Animator>())
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            if (!enableLights)
                foreach (var light in riverValleyPortalInstance.GetComponentsInChildren<Light>())
                    light.enabled = false;
        }

        /// <summary>在切换阶段、重开和离场时立即撤下粒子表现。</summary>
        private void ClearRiverValleyPortalEffect()
        {
            if (!riverValleyPortalInstance) return;
            riverValleyPortalInstance.SetActive(false);
            Release(riverValleyPortalInstance);
            riverValleyPortalInstance = null;
        }
    }
}
