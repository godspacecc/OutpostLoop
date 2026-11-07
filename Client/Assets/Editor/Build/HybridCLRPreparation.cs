#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

namespace Game.Editor
{
    /// <summary>完整包共用的 HybridCLR 准备顺序；平台回调仅负责预备 Player 与平台 AOT 检查。</summary>
    public static class HybridCLRPreparation
    {
        public static IReadOnlyList<string> Run(BuildTarget target, BuildStageTiming timing,
            Action buildPreparatoryPlayer, Action validatePreparatoryAot = null)
        {
            timing.Run("GeneratePrePlayer", () => HybridCLRBuildUtility.GeneratePrePlayer(target));
            timing.Run("BuildPreparatoryPlayer", buildPreparatoryPlayer);
            if (validatePreparatoryAot != null) timing.Run("ValidatePreparatoryAot", validatePreparatoryAot);
            timing.Run("GeneratePostPlayer", () => HybridCLRBuildUtility.GeneratePostPlayer(target));
            var copied = timing.Run("CopyGeneratedDlls", () => HybridCLRBuildUtility.CopyGeneratedDlls(target, false));
            timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);
            return copied;
        }
    }
}
#endif
