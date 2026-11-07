using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Game
{
    /// <summary>可热更新的整包更新检查策略，只获取有界描述并生成展示候选，不下载或安装文件。</summary>
    public static class ApplicationUpdateRequest
    {
        public static async UniTask<ApplicationUpdateCandidate> CheckAsync(string server, string package,
            string version, int code, CancellationToken cancellation)
        {
            using var handler = new LimitedTextHandler();
            using var request = new UnityWebRequest(server.TrimEnd('/') + "/app-update.json", "GET", handler, null);
            request.timeout = 5;
            request.redirectLimit = 0;
            request.SetRequestHeader("Cache-Control", "no-cache");
            await request.SendWebRequest().ToUniTask(cancellationToken: cancellation);
            return Parse(handler.Text, package, version, code);
        }

        /// <summary>校验描述后只返回新版本；格式不合法与“已经最新”严格区分。</summary>
        public static ApplicationUpdateCandidate Parse(string json, string package, string version, int code)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536)
                throw new InvalidOperationException("安装包版本描述为空或过长");
            Document value;
            try { value = JsonUtility.FromJson<Document>(json); }
            catch (ArgumentException e) { throw new InvalidOperationException("安装包版本描述格式无效", e); }
            if (value == null || value.schemaVersion != 1 || value.platform != "Android" || value.packageName != package ||
                value.versionCode <= 0 || value.sizeBytes <= 0 || value.sizeBytes > 8L * 1024 * 1024 * 1024 ||
                !Regex.IsMatch(value.apkFileName ?? "", @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,150}\.apk\z") ||
                !Regex.IsMatch(value.sha256 ?? "", @"\A[0-9a-fA-F]{64}\z") ||
                !Regex.IsMatch(value.versionName ?? "", @"\A[0-9]+\.[0-9]+\.[0-9]+\z") ||
                !Version.TryParse(value.versionName, out var next) || !Version.TryParse(version, out var current))
                throw new InvalidOperationException("安装包版本描述不可用");
            return value.versionCode >= code && next > current
                ? new ApplicationUpdateCandidate { versionName = value.versionName, sizeBytes = value.sizeBytes } : null;
        }

        [Serializable]
        private sealed class Document
        {
            public int schemaVersion, versionCode;
            public string platform, packageName, versionName, apkFileName, sha256;
            public long sizeBytes;
        }

        private sealed class LimitedTextHandler : DownloadHandlerScript
        {
            private readonly MemoryStream bytes = new MemoryStream();
            public LimitedTextHandler() : base(new byte[8192]) { }
            public string Text => Encoding.UTF8.GetString(bytes.ToArray());
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (data == null || length < 0 || bytes.Length + length > 65536) return false;
                bytes.Write(data, 0, length);
                return true;
            }
            public override void Dispose() { bytes.Dispose(); base.Dispose(); }
        }
    }
}
