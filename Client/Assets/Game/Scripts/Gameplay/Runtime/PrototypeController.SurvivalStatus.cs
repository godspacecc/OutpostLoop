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
            if (!CanShowSurvivalHud() || survivalPackOpen && inventoryRoot)
            {
                if (survivalStatusView) survivalStatusView.gameObject.SetActive(false);
                return;
            }
            if (!survivalStatusView && !LoadSurvivalStatusView()) return;

            survivalStatusView.gameObject.SetActive(true);
            var state = scavengeRun.Survival;
            bool driving = scavengeRun.Driving;
            survivalStatusView.Render(state, driving, scavengeRun.VehicleSpeed, SurvivalInventoryOpen);
            var action = PerceptionRect(PerceptionGroup.Action);
            var body = PerceptionRect(PerceptionGroup.Body);
            var equipment = PerceptionRect(PerceptionGroup.Equipment);
            var pack = PerceptionGeometry.Pack(lastPerceptionArea);
            survivalStatusView.ApplyLayout(action, body, equipment, pack, PerceptionRect(PerceptionGroup.Interaction),
                ShowPerceptionGroup(PerceptionGroup.Action, action), ShowPerceptionGroup(PerceptionGroup.Body, body),
                !driving && ShowPerceptionGroup(PerceptionGroup.Equipment, equipment),
                !driving && !SurvivalInventoryOpen, driving, state);
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
            survivalStatusView.InitializeLayout();
            survivalStatusView.Pack.onClick.AddListener(() =>
            {
                if (CanShowSurvivalHud() && !scavengeRun.Driving) OpenSurvivalPack();
            });
            return true;
        }

        /// <summary>背包入口独立命中，纯读数不会拦截世界点击。</summary>
        private bool SurvivalStatusContains(Vector2 hudPoint) =>
            CanShowSurvivalHud() && !scavengeRun.Driving && !SurvivalInventoryOpen &&
            PerceptionGeometry.Pack(PerceptionGeometry.ScreenArea).Contains(hudPoint);
    }
}
