using System.IO;
using UnityEngine;
namespace Game
{
    /// <summary>运行期日志目录；编辑器按启动批次归档，Player 使用本应用持久目录。</summary>
    public static class RuntimeLogPaths
    {
        /// <summary>当前进程固定目录，避免每条错误生成不同批次。</summary>
        public static readonly string Directory = CreateDirectoryPath();
        /// <summary>只在主线程初始化时读取 Unity 路径。</summary>
        private static string CreateDirectoryPath()
        {
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/RuntimeLogs/"+System.DateTime.Now.ToString("yyyyMMdd-HHmmss")));
#else
            return Path.Combine(Application.persistentDataPath,"Logs");
#endif
        }
    }
}
