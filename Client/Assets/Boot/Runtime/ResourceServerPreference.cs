using System;
using System.IO;
using UnityEngine;

namespace Boot
{
    /// <summary>
    /// 持有玩家选择的资源服务器覆盖地址；只负责 HTTP/HTTPS 地址校验与原子文件替换，
    /// 不负责连接服务器或决定资源版本。
    /// </summary>
    public static class ResourceServerPreference
    {
        public const string OverrideFileName = "resource-server.json";
        private const int FileVersion = 1;

        public static bool TryLoad(out string resourceServer, out string diagnostic)
        {
            return TryLoad(Application.persistentDataPath, out resourceServer, out diagnostic);
        }

        public static bool TrySave(string value, out string resourceServer, out string diagnostic)
        {
            return TrySave(value, Application.persistentDataPath, out resourceServer, out diagnostic);
        }

        public static bool TryReset(out string diagnostic)
        {
            return TryReset(Application.persistentDataPath, out diagnostic);
        }

        /// <summary>校验并规范化资源服务器根地址；末尾斜杠不参与持久化。</summary>
        public static bool TryNormalize(string value, out string resourceServer)
        {
            resourceServer = string.Empty;
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                return false;
            }

            resourceServer = value.Trim().TrimEnd('/');
            return true;
        }

        public static bool TryLoad(string persistentRoot, out string resourceServer, out string diagnostic)
        {
            resourceServer = string.Empty;
            diagnostic = null;
            string path = GetOverridePath(persistentRoot);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                var data = JsonUtility.FromJson<OverrideData>(File.ReadAllText(path));
                if (data == null || data.version != FileVersion ||
                    !TryNormalize(data.resourceServer, out resourceServer))
                {
                    diagnostic = "override-invalid";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                diagnostic = exception.GetType().Name;
                return false;
            }
        }

        /// <summary>写入前先完整校验；失败时删除临时文件并保留上一次有效配置。</summary>
        public static bool TrySave(
            string value,
            string persistentRoot,
            out string resourceServer,
            out string diagnostic)
        {
            diagnostic = null;
            if (!TryNormalize(value, out resourceServer))
            {
                diagnostic = "resource-server";
                return false;
            }

            string path = GetOverridePath(persistentRoot);
            if (string.IsNullOrWhiteSpace(path))
            {
                diagnostic = "persistence-unavailable";
                return false;
            }

            string directory = Path.GetDirectoryName(path);
            string tempPath = path + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(tempPath, JsonUtility.ToJson(new OverrideData
                {
                    version = FileVersion,
                    resourceServer = resourceServer
                }, true));
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null, true);
                }
                else
                {
                    File.Move(tempPath, path);
                }

                return true;
            }
            catch (Exception exception)
            {
                diagnostic = exception.GetType().Name;
                TryDeleteTemporaryFile(tempPath);
                return false;
            }
        }

        public static bool TryReset(string persistentRoot, out string diagnostic)
        {
            diagnostic = null;
            string path = GetOverridePath(persistentRoot);
            if (string.IsNullOrWhiteSpace(path))
            {
                diagnostic = "persistence-unavailable";
                return false;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return true;
            }
            catch (Exception exception)
            {
                diagnostic = exception.GetType().Name;
                return false;
            }
        }

        private static string GetOverridePath(string persistentRoot)
        {
            return string.IsNullOrWhiteSpace(persistentRoot)
                ? null
                : Path.Combine(persistentRoot, "Network", OverrideFileName);
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ResourceServerPreference] 删除临时文件失败：{exception.GetType().Name}");
            }
        }

        [Serializable]
        private sealed class OverrideData
        {
            public int version;
            public string resourceServer;
        }
    }
}
