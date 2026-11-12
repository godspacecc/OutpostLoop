using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game
{
    /// <summary>为编辑器 Play 会话提供白名单指令入口，执行仍由运行时系统负责。</summary>
    public sealed class FoundationDebugCommandWindow : EditorWindow
    {
        // 保留输入和结果，便于连续诊断同一会话。
        private string input = "#help";
        private string output = string.Empty;

        [MenuItem("Tools/诊断/运行调试指令")]
        private static void Open() => GetWindow<FoundationDebugCommandWindow>("调试指令");

        private void OnGUI()
        {
            input = EditorGUILayout.TextField("指令", input);
            bool ready = EditorApplication.isPlaying && FoundationRuntime.IsInitialized;
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("执行")) Execute();
            }
            if (!ready) EditorGUILayout.HelpBox("从 Boot 进入 Play 后可执行指令。", MessageType.Info);
            EditorGUILayout.SelectableLabel(output, EditorStyles.wordWrappedLabel, GUILayout.ExpandHeight(true));
        }

        /// <summary>显示系统写入日志的结果，保持指令行为与代码调用一致。</summary>
        private void Execute()
        {
            var architecture = Game.Interface;
            architecture.GetSystem<DebugCommandSystem>().ExecuteAsync(input).GetAwaiter().GetResult();
            output = architecture.GetModel<RuntimeLogModel>().Snapshot().LastOrDefault().Message;
        }
    }
}
