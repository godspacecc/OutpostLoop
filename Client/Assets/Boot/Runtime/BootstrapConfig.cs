using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Boot
{
    /// <summary>
    /// Boot 稳定程序集的外置配置入口，优先读取玩家覆盖地址，否则读取 StreamingAssets/hostConfig.json。
    /// </summary>
    public sealed class BootstrapConfig
    {
        public const string FileName = "hostConfig.json";

        public string resourceServer;

        public string ResourceServer => resourceServer;

        private BootstrapConfig(string resourceServer)
        {
            this.resourceServer = resourceServer;
        }

        public static BootstrapConfig Load()
        {
            return ResourceServerPreference.TryLoad(out string server, out _) ? new BootstrapConfig(server) : LoadDefault();
        }

        /// <summary>读取包内默认值，恢复默认时不使用玩家覆盖。</summary>
        public static BootstrapConfig LoadDefault()
        {
            string path = Path.Combine(Application.streamingAssetsPath, FileName);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"启动配置文件不存在：{path}\n" +
                    "请确保在 StreamingAssets 目录下创建 hostConfig.json");
            }

            string json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(
                    $"启动配置文件为空：{path}");
            }

            return FromJson(json);
        }

        /// <summary>异步读取 StreamingAssets；Android APK 内路径必须通过 UnityWebRequest 访问。</summary>
        public static async UniTask<BootstrapConfig> LoadAsync()
        {
            return ResourceServerPreference.TryLoad(out string server, out _) ? new BootstrapConfig(server) : await LoadDefaultAsync();
        }

        /// <summary>异步读取包内默认值，兼容 Android APK 路径。</summary>
        public static async UniTask<BootstrapConfig> LoadDefaultAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            using (var request = UnityWebRequest.Get(path))
            {
                await request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new IOException(
                        $"启动配置读取失败：{path}，result={request.result}，error={request.error}");
                }

                string json = request.downloadHandler?.text;
                if (string.IsNullOrWhiteSpace(json))
                    throw new InvalidOperationException($"启动配置文件为空：{path}");
                return FromJson(json);
            }
#else
            return LoadDefault();
#endif
        }

        internal static BootstrapConfig FromJson(string json)
        {
            try
            {
                var data = JsonUtility.FromJson<JsonData>(json);

                if (data == null)
                {
                    throw new InvalidOperationException("JSON 解析结果为 null");
                }

                if (string.IsNullOrWhiteSpace(data.resourceServer))
                {
                    throw new InvalidOperationException("resourceServer 字段为空或未定义");
                }

                string server = data.resourceServer.Trim();
                if (!server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !server.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"resourceServer 必须是绝对 HTTP/HTTPS URL，实际值：{server}");
                }

                return new BootstrapConfig(server);
            }
            catch (ArgumentException e)
            {
                throw new InvalidOperationException(
                    $"JSON 解析失败：{e.Message}\n原始内容：{json}", e);
            }
        }

        [Serializable]
        private class JsonData
        {
            public string resourceServer;
        }
    }
}
