using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Debug = UnityEngine.Debug;
using TextAsset = UnityEngine.TextAsset;
using YooAsset;
using HybridCLR;

namespace Boot
{
    /// <summary>
    /// 参与 Boot 启动链的 CodeLoader，在热更代码加载前完成资源、配置或界面衔接。
    /// </summary>
    public static class CodeLoader
    {
        private const string AOT_LIST_ADDRESS = "HybridCLRAssemblyList.bytes";
        private const string GAME_DLL_ADDRESS = "Game.dll.bytes";

#if UNITY_INCLUDE_TESTS
        public static Func<string, UniTask<byte[]>> TestLoadBytesAsync { get; set; }
        public static Func<string, byte[], int> TestLoadAotMetadataResult { get; set; }
        public static Func<byte[], Assembly> TestLoadGameAssembly { get; set; }

        public static void ResetTestHooks()
        {
            TestLoadBytesAsync = null;
            TestLoadAotMetadataResult = null;
            TestLoadGameAssembly = null;
        }

#endif

        public static async UniTask<BootOperationResult> LoadAsync(ResourcePackage package)
        {
#if UNITY_INCLUDE_TESTS
            if (TestLoadBytesAsync != null)
            {
                return await LoadDllsFromTestHooks();
            }
#endif

#if UNITY_EDITOR
            if (BootResourceStartupState.TryGet(out var sourceInfo) && sourceInfo.Source == BootResourceSource.EditorSource)
                return BootOperationResult.Success();
#endif
            if (package == null)
            {
                Debug.LogWarning("[CodeLoader] ResourcePackage 为空。");
                return BootOperationResult.Failed(BootFailure.Create(
                    BootFailureCode.RequiredResourceMissing,
                    BootFailureStage.GameAssembly,
                    BootRetryScope.None,
                    DiagnosticFields(("resourcePackage", "OutpostLoop"))));
            }

#if UNITY_EDITOR
            return BootOperationResult.Success();
#else
            return await LoadDllsFromPackage(package);
#endif
        }

#if UNITY_INCLUDE_TESTS
        private static async UniTask<BootOperationResult> LoadDllsFromTestHooks()
        {
            byte[] aotListBytes = await LoadTestBytes(AOT_LIST_ADDRESS);
            if (aotListBytes == null)
            {
                return Failure(
                    BootFailureCode.AotListInvalid,
                    BootFailureStage.AotList,
                    ("dllName", AOT_LIST_ADDRESS));
            }

            string content = System.Text.Encoding.UTF8.GetString(aotListBytes);
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var dlls = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                string dllName = line.Trim();
                if (string.IsNullOrEmpty(dllName) || dllName.StartsWith("//"))
                    continue;

                if (dllName.Equals("Game.dll", StringComparison.OrdinalIgnoreCase) ||
                    dllName.Equals("Game", StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(BootFailureCode.AotListInvalid, BootFailureStage.AotList, ("dllName", "Game.dll"));
                }

                if (!dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    dllName += ".dll";

                if (!seen.Add(dllName))
                    return Failure(BootFailureCode.AotListInvalid, BootFailureStage.AotList, ("dllName", dllName));

                dlls.Add(dllName);
            }

            if (dlls.Count == 0)
                return Failure(BootFailureCode.AotListInvalid, BootFailureStage.AotList, ("dllName", AOT_LIST_ADDRESS));

            foreach (var dllName in dlls)
            {
                byte[] aotBytes = await LoadTestBytes($"{dllName}.bytes");
                if (aotBytes == null)
                    return Failure(BootFailureCode.AotDllMissing, BootFailureStage.AotMetadata, ("dllName", dllName));

                try
                {
                    int metadataResult = TestLoadAotMetadataResult?.Invoke(dllName, aotBytes) ?? 0;
                    if (metadataResult != 0)
                    {
                        return Failure(
                            BootFailureCode.AotMetadataRejected,
                            BootFailureStage.AotMetadata,
                            ("dllName", dllName),
                            ("metadataResult", metadataResult.ToString()));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return BootOperationResult.Failed(BootFailure.Create(
                        BootFailureCode.AotMetadataException,
                        BootFailureStage.AotMetadata,
                        BootRetryScope.None,
                        DiagnosticFields(
                            ("dllName", dllName),
                            ("exceptionType", e.GetType().FullName),
                            ("hResult", e.HResult.ToString()))));
                }
            }

            byte[] gameBytes = await LoadTestBytes(GAME_DLL_ADDRESS);
            if (gameBytes == null)
                return Failure(BootFailureCode.GameDllMissing, BootFailureStage.GameAssembly, ("dllName", "Game.dll"));

            try
            {
                if (TestLoadGameAssembly != null)
                {
                    TestLoadGameAssembly(gameBytes);
                }
                else
                {
                    Assembly.Load(gameBytes);
                }

                return BootOperationResult.Success();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return BootOperationResult.Failed(BootFailure.Create(
                    BootFailureCode.GameDllLoadFailed,
                    BootFailureStage.GameAssembly,
                    BootRetryScope.None,
                    DiagnosticFields(
                        ("dllName", "Game.dll"),
                        ("exceptionType", e.GetType().FullName),
                        ("hResult", e.HResult.ToString()))));
            }
        }

        private static async UniTask<byte[]> LoadTestBytes(string address)
        {
            if (TestLoadBytesAsync == null)
                return null;

            return await TestLoadBytesAsync(address);
        }

        private static BootOperationResult Failure(
            BootFailureCode code,
            BootFailureStage stage,
            params (string key, string value)[] fields)
        {
            return BootOperationResult.Failed(BootFailure.Create(code, stage, BootRetryScope.None, DiagnosticFields(fields)));
        }
#endif

#if !UNITY_EDITOR
        private static async UniTask<BootOperationResult> LoadDllsFromPackage(ResourcePackage package)
        {
            var compatibility = await HostCompatibility.ValidateAsync(package);
            if (!compatibility.Succeeded) return compatibility;
            var aotListResult = await LoadAotList(package);
            if (!aotListResult.Result.Succeeded)
                return aotListResult.Result;

            foreach (var dllName in aotListResult.dlls)
            {
                BootOperationResult loaded = await LoadAotMetadata(package, dllName);
                if (!loaded.Succeeded)
                    return loaded;
            }

            return await LoadGameDll(package);
        }

        private static async UniTask<(BootOperationResult Result, List<string> dlls)> LoadAotList(ResourcePackage package)
        {
            var loadResult = await LoadBytesAsync(package, AOT_LIST_ADDRESS, BootFailureCode.AotListInvalid, BootFailureStage.AotList);
            if (!loadResult.Result.Succeeded)
                return (loadResult.Result, null);

            byte[] bytes = loadResult.Bytes;
            if (bytes == null)
            {
                return (Failure(BootFailureCode.AotListInvalid, BootFailureStage.AotList, ("dllName", AOT_LIST_ADDRESS)), null);
            }

            string content = System.Text.Encoding.UTF8.GetString(bytes);
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var dlls = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                string dllName = line.Trim();
                if (string.IsNullOrEmpty(dllName) || dllName.StartsWith("//"))
                    continue;

                if (dllName.Equals("Game.dll", StringComparison.OrdinalIgnoreCase) ||
                    dllName.Equals("Game", StringComparison.OrdinalIgnoreCase))
                {
                    return (Failure(BootFailureCode.AotListInvalid, BootFailureStage.AotList, ("dllName", "Game.dll")), null);
                }

                if (!dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    dllName += ".dll";

                if (!seen.Add(dllName))
                {
                    return (Failure(BootFailureCode.AotListInvalid, BootFailureStage.AotList, ("dllName", dllName)), null);
                }

                dlls.Add(dllName);
            }

            if (dlls.Count == 0)
            {
                return (Failure(BootFailureCode.AotListInvalid, BootFailureStage.AotList, ("dllName", AOT_LIST_ADDRESS)), null);
            }
            return (BootOperationResult.Success(), dlls);
        }

        private static async UniTask<BootOperationResult> LoadAotMetadata(ResourcePackage package, string dllName)
        {
            var loadResult = await LoadBytesAsync(package, $"{dllName}.bytes", BootFailureCode.AotDllMissing, BootFailureStage.AotMetadata);
            if (!loadResult.Result.Succeeded)
                return loadResult.Result;

            byte[] bytes = loadResult.Bytes;

            try
            {
                var result = RuntimeApi.LoadMetadataForAOTAssembly(bytes, HomologousImageMode.SuperSet);
                if (result != LoadImageErrorCode.OK)
                {
                    return Failure(
                        BootFailureCode.AotMetadataRejected,
                        BootFailureStage.AotMetadata,
                        ("dllName", dllName),
                        ("metadataResult", result.ToString()));
                }
                return BootOperationResult.Success();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return BootOperationResult.Failed(BootFailure.Create(
                    BootFailureCode.AotMetadataException,
                    BootFailureStage.AotMetadata,
                    BootRetryScope.None,
                    DiagnosticFields(
                        ("dllName", dllName),
                        ("exceptionType", e.GetType().FullName),
                        ("hResult", e.HResult.ToString()))));
            }
        }

        private static async UniTask<BootOperationResult> LoadGameDll(ResourcePackage package)
        {
            var loadResult = await LoadBytesAsync(package, GAME_DLL_ADDRESS, BootFailureCode.GameDllMissing, BootFailureStage.GameAssembly);
            if (!loadResult.Result.Succeeded)
                return loadResult.Result;

            byte[] bytes = loadResult.Bytes;

            try
            {
                Assembly.Load(bytes);
                return BootOperationResult.Success();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return BootOperationResult.Failed(BootFailure.Create(
                    BootFailureCode.GameDllLoadFailed,
                    BootFailureStage.GameAssembly,
                    BootRetryScope.None,
                    DiagnosticFields(
                        ("dllName", "Game.dll"),
                        ("exceptionType", e.GetType().FullName),
                        ("hResult", e.HResult.ToString()))));
            }
        }

        private static async UniTask<(BootOperationResult Result, byte[] Bytes)> LoadBytesAsync(
            ResourcePackage package,
            string address,
            BootFailureCode missingCode,
            BootFailureStage stage)
        {
            AssetHandle handle = null;
            try
            {
                handle = package.LoadAssetAsync<TextAsset>(address);
                await handle.WaitAsync();

                if (handle.Status != EOperationStatus.Succeed)
                {
                    Debug.LogError($"[CodeLoader] 资源加载失败: {address}");
                    return (Failure(missingCode, stage, ("dllName", address), ("operationStatus", handle.Status.ToString())), null);
                }

                var textAsset = handle.AssetObject as TextAsset;
                if (textAsset == null)
                {
                    Debug.LogError($"[CodeLoader] 资源类型错误: {address}");
                    return (Failure(missingCode, stage, ("dllName", address)), null);
                }

                return (BootOperationResult.Success(), (byte[])textAsset.bytes.Clone());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return (BootOperationResult.Failed(BootFailure.Unexpected(e, stage)), null);
            }
            finally
            {
                handle?.Release();
            }
        }

        private static BootOperationResult Failure(
            BootFailureCode code,
            BootFailureStage stage,
            params (string key, string value)[] fields)
        {
            return BootOperationResult.Failed(BootFailure.Create(code, stage, BootRetryScope.None, DiagnosticFields(fields)));
        }
#endif

        private static IReadOnlyDictionary<string, string> DiagnosticFields(params (string key, string value)[] fields)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                result[field.key] = field.value;
            }

            return result;
        }
    }
}
