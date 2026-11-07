using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;

namespace Boot
{
    /// <summary>宿主与热更资源的配对身份；每次完整生成创建新身份，HostUpdate 必须继承所选基线。</summary>
    [Serializable]
    public sealed class HostCompatibilityDescriptor
    {
        public const string AssetAddress = "HostCompatibility.bytes";
        public const string EmbeddedFileName = "host-compatibility.json";
        public int schemaVersion;
        public string hostId;
        public string platform;

        /// <summary>缺失、未知格式、平台错误及不同宿主均拒绝；该身份用于兼容性，不代替内容签名。</summary>
        public static bool Matches(string installedJson, string resourceJson, string platform, out string reason)
        {
            reason = "宿主兼容描述缺失或无效";
            try
            {
                if (string.IsNullOrWhiteSpace(installedJson) || string.IsNullOrWhiteSpace(resourceJson) ||
                    installedJson.Length > 4096 || resourceJson.Length > 4096) return false;
                var installed = JsonUtility.FromJson<HostCompatibilityDescriptor>(installedJson);
                var required = JsonUtility.FromJson<HostCompatibilityDescriptor>(resourceJson);
                if (!Valid(installed) || !Valid(required)) return false;
                if (installed.platform != platform || required.platform != platform)
                { reason = "资源平台与宿主不一致"; return false; }
                if (!string.Equals(installed.hostId, required.hostId, StringComparison.Ordinal))
                { reason = "资源要求不同宿主，请更新完整安装包"; return false; }
                reason = string.Empty;
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private static bool Valid(HostCompatibilityDescriptor value) => value != null && value.schemaVersion == 1 &&
            Regex.IsMatch(value.hostId ?? "", @"\A[0-9a-f]{32}\z") &&
            (value.platform == "Android" || value.platform == "StandaloneWindows64");
    }

    /// <summary>在资源准备完成和 DLL 加载前读取兼容契约；不匹配时不执行任何 AOT 元数据或 Game 代码。</summary>
    public static class HostCompatibility
    {
        public static UniTask<BootOperationResult> ValidateAsync(ResourcePackage package)
        {
#if UNITY_EDITOR
            // 编辑器使用源码程序集；显式 ValidateSourcesAsync 测试与 Player 共用拒绝路径。
            return UniTask.FromResult(BootOperationResult.Success());
#else
            string platform = Application.platform == RuntimePlatform.Android ? "Android" :
                Application.platform == RuntimePlatform.WindowsPlayer ? "StandaloneWindows64" : "Unsupported";
            return ValidateSourcesAsync(ReadInstalledAsync, () => ReadResourceAsync(package), platform);
#endif
        }

        /// <summary>先读取宿主再读取资源契约，任何读取或比较失败均拒绝加载；由生产路径及故障注入共用。</summary>
        public static async UniTask<BootOperationResult> ValidateSourcesAsync(Func<UniTask<string>> installed,
            Func<UniTask<string>> resource, string platform)
        {
            try
            {
                string local = await installed();
                string required = await resource();
                return HostCompatibilityDescriptor.Matches(local, required, platform, out string reason)
                    ? BootOperationResult.Success() : Failure(reason);
            }
            catch (Exception exception) { return Failure("读取宿主兼容描述失败：" + exception.GetType().Name); }
        }

        private static async UniTask<string> ReadInstalledAsync()
        {
            string path = Path.Combine(Application.streamingAssetsPath, HostCompatibilityDescriptor.EmbeddedFileName);
            if (!path.Contains("://")) return File.ReadAllText(path);
            using var request = UnityWebRequest.Get(path);
            request.timeout = 10;
            await request.SendWebRequest().ToUniTask();
            return request.downloadHandler.text;
        }

        private static async UniTask<string> ReadResourceAsync(ResourcePackage package)
        {
            if (package == null || !package.CheckLocationValid(HostCompatibilityDescriptor.AssetAddress)) return null;
            var handle = package.LoadAssetAsync<TextAsset>(HostCompatibilityDescriptor.AssetAddress);
            try
            {
                await handle.WaitAsync();
                return (handle.AssetObject as TextAsset)?.text;
            }
            finally { handle.Release(); }
        }

        private static BootOperationResult Failure(string reason) => BootOperationResult.Failed(BootFailure.Create(
            BootFailureCode.HostIncompatible, BootFailureStage.ResourceValidation, BootRetryScope.None,
            new Dictionary<string, string> { ["compatibility"] = reason }));
    }
}
