using System.IO;
using UnityEngine;
namespace Boot
{
    /// <summary>编辑器验证日志归本仓库产物目录；Player 日志保留应用独立持久目录。</summary>
    public static class BootLogPaths
    {
        public static string Root
        {
            get
            {
#if UNITY_EDITOR
                return Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/BootMainBuild-20261107/Runtime"));
#else
                return Application.persistentDataPath;
#endif
            }
        }
    }
}
