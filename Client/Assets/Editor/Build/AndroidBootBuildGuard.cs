#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Game.Editor
{
    /// <summary>锁定 Android 首包包含的 Boot 输入，防止旧宿主接收要求新 Boot 的代码和资源。</summary>
    public static class AndroidBootBuildGuard
    {
        /// <summary>所有拒绝路径给出同一可执行操作，不允许通过重新记录当前指纹跳过首包。</summary>
        private const string RebuildHint = "请执行 Tools/打包/Android/完整打包 或 Tools/打包/Android/启动包（首次下载资源），并在设备安装新 APK 后再更新代码和资源。";

        /// <summary>按项目相对路径和内容计算指纹，覆盖 Boot 目录、根 meta 及 Boot 场景；不使用时间戳。</summary>
        public static string ComputeFingerprint(string projectRoot)
        {
            string root = Path.GetFullPath(projectRoot);
            string bootDirectory = Path.Combine(root, "Assets", "Boot");
            string scenePath = Path.Combine(root, "Assets", "Game", "Scenes", "Boot.unity");
            if (!Directory.Exists(bootDirectory) || !File.Exists(scenePath))
                throw new InvalidOperationException("Boot 目录或启动场景缺失，无法核对首包。" + RebuildHint);

            var files = new List<string>(Directory.GetFiles(bootDirectory, "*", SearchOption.AllDirectories));
            if (files.Count == 0)
                throw new InvalidOperationException("Boot 目录为空，无法核对首包。" + RebuildHint);
            files.Add(scenePath);
            if (File.Exists(bootDirectory + ".meta")) files.Add(bootDirectory + ".meta");
            if (File.Exists(scenePath + ".meta")) files.Add(scenePath + ".meta");
            files.Sort(StringComparer.Ordinal);

            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            using (var hash = SHA256.Create())
            {
                foreach (string file in files)
                {
                    // 长度编码的路径和定长内容哈希可区分增删、重命名及同长度内容修改。
                    writer.Write(file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/'));
                    using (var stream = File.OpenRead(file)) writer.Write(hash.ComputeHash(stream));
                }
                writer.Flush();
                buffer.Position = 0;
                return "boot-v1:" + BitConverter.ToString(hash.ComputeHash(buffer)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

    }
}
#endif
