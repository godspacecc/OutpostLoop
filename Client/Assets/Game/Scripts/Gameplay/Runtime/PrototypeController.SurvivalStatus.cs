using Game;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 状态层在物品界面打开时保留，由现有模态条件统一隐藏。
        private const string SurvivalStatusAddress = "Prefabs/UI/RiverValleyStatus";
        private SurvivalStatusView survivalStatusView;
        private bool survivalStatusLoadFailed;

        /// <summary>更新状态层，保持世界内背包入口可用。</summary>
        private void UpdateSurvivalStatus()
        {
            if (!CanShowSurvivalHud())
            {
                if (survivalStatusView) survivalStatusView.gameObject.SetActive(false);
                return;
            }
            if (!survivalStatusView && !LoadSurvivalStatusView()) return;

            survivalStatusView.gameObject.SetActive(true);
            survivalStatusView.Render(scavengeRun.Survival, scavengeRun.Driving, scavengeRun.VehicleSpeed, SurvivalInventoryOpen);
        }

        private bool LoadSurvivalStatusView()
        {
            if (survivalStatusLoadFailed) return false;
            var prefab = ResourceLoader.LoadAsset<GameObject>(SurvivalStatusAddress);
            if (!prefab || !prefab.TryGetComponent<SurvivalStatusView>(out var template))
            {
                survivalStatusLoadFailed = true;
                Debug.LogError("河谷镇状态预制体缺失：" + SurvivalStatusAddress, this);
                return false;
            }

            survivalStatusView = Instantiate(template, transform, false);
            survivalStatusView.name = "RiverValleyStatusCanvas";
            survivalStatusView.Pack.onClick.AddListener(() =>
            {
                if (CanShowSurvivalHud() && !scavengeRun.Driving) OpenSurvivalPack();
            });
            return true;
        }

        /// <summary>状态层挡住对世界的点击，命中边界取自可编辑预制体。</summary>
        private bool SurvivalStatusContains(Vector2 cursor, Vector2 hudPoint) =>
            survivalStatusView
                ? survivalStatusView.isActiveAndEnabled &&
                    RectTransformUtility.RectangleContainsScreenPoint(survivalStatusView.Panel, cursor)
                : SurvivalInfo.Contains(hudPoint);
    }
}
