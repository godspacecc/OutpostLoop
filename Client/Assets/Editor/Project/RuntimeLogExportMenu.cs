using Game;
using QFramework;
using UnityEditor;
using UnityEngine.SceneManagement;
namespace OutpostLoop.Editor
{
    /// <summary>从通用诊断菜单导出当前运行会话日志。</summary>
    public static class RuntimeLogExportMenu
    {
        /// <summary>不隐式创建架构，仅导出当前运行会话的日志快照。</summary>
        [MenuItem("Tools/诊断/导出运行时日志")]
        public static void Export()
        {
            if(!FoundationRuntime.IsInitialized)return;
            Game.Game.Interface.GetSystem<RuntimeLogSystem>().DrainPending();
            var path=RuntimeLogExportUtility.Export(Game.Game.Interface.GetModel<RuntimeLogModel>().Snapshot(),SceneManager.GetActiveScene().name);
            UnityEngine.Debug.Log("运行时日志已导出："+path);
        }
        /// <summary>仅在基础会话就绪时启用导出。</summary>
        [MenuItem("Tools/诊断/导出运行时日志",true)]
        private static bool CanExport()=>FoundationRuntime.IsInitialized;
    }
}
