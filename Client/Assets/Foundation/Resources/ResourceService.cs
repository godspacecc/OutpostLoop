using System;
using System.Collections;
using System.IO;
using UnityEngine;
using YooAsset;
namespace OutpostLoop.Resources
{
    /// <summary>资源句柄由调用方释放，不泄漏底层引用。</summary>
    public sealed class ResourceLease<T> : IDisposable where T : UnityEngine.Object
    {
        private AssetHandle handle;
        public T Asset { get; private set; }
        internal ResourceLease(AssetHandle value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var asset = value.AssetObject as T;
            if (asset == null) { value.Release(); throw new InvalidOperationException("资源为空或类型不匹配。"); }
            handle = value; Asset = asset;
        }
        public void Dispose() { handle?.Release(); handle = null; Asset = null; }
    }
    /// <summary>独立资源包入口；只读取已有清单，绝不生成模拟包。</summary>
    public sealed class ResourceService
    {
        public const string PackageName = "OutpostLoop";
        private ResourcePackage package;
        public bool IsReady { get; private set; }
        /// <summary>明确选择已有编辑器清单或者内置离线包，缺失时直接报错。</summary>
        public IEnumerator Initialize(bool editorExistingManifest, string existingPackageRoot = null)
        {
            if (package != null) throw new InvalidOperationException("资源服务已初始化或尚未关闭。");
            var routine = InitializeCore(editorExistingManifest, existingPackageRoot);
            Exception failure = null;
            while (true)
            {
                bool more;
                try { more = routine.MoveNext(); }
                catch (Exception error) { failure = error; break; }
                if (!more) break;
                yield return routine.Current;
            }
            if (failure != null)
            {
                yield return Shutdown();
                throw new InvalidOperationException("资源初始化失败，已释放本次资源包。", failure);
            }
        }
        // 捕获同步 API 错误和异步操作失败，统一由外层协程完成清理。
        private IEnumerator InitializeCore(bool editorExistingManifest, string existingPackageRoot)
        {
            if (editorExistingManifest && (string.IsNullOrEmpty(existingPackageRoot) || !Directory.Exists(existingPackageRoot)))
                throw new InvalidOperationException("缺少已有 YooAsset 编辑器清单目录；禁止自动构建模拟清单。");
            YooAssets.Initialize();
            package = YooAssets.CreatePackage(PackageName);
            InitializeParameters parameters;
            if (editorExistingManifest)
                parameters = new EditorSimulateModeParameters { EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(existingPackageRoot) };
            else
                parameters = new OfflinePlayModeParameters { BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters() };
            var init = package.InitializeAsync(parameters); yield return init;
            if (init.Status != EOperationStatus.Succeed)
            {
                var error = init.Error;
                throw new InvalidOperationException(error);
            }
            var version = package.RequestPackageVersionAsync(); yield return version;
            if (version.Status != EOperationStatus.Succeed)
            {
                var error = version.Error;
                throw new InvalidOperationException(error);
            }
            var manifest = package.UpdatePackageManifestAsync(version.PackageVersion); yield return manifest;
            if (manifest.Status != EOperationStatus.Succeed)
            {
                var error = manifest.Error;
                throw new InvalidOperationException(error);
            }
            IsReady = true;
        }
        /// <summary>同步加载必须在初始化完成后调用，失败保留底层原因。</summary>
        public ResourceLease<T> Load<T>(string address) where T : UnityEngine.Object
        {
            if (!IsReady) throw new InvalidOperationException("资源包尚未准备完成。");
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("资源地址不能为空。", nameof(address));
            var handle = package.LoadAssetSync<T>(address);
            if (handle.Status != EOperationStatus.Succeed) { var error = handle.LastError; handle.Release(); throw new InvalidOperationException(error); }
            return new ResourceLease<T>(handle);
        }
        public byte[] LoadBytes(string address) { using (var lease = Load<TextAsset>(address)) return lease.Asset.bytes; }
        /// <summary>调用方须先释放全部租约，再等待关闭完成。</summary>
        public IEnumerator Shutdown()
        {
            IsReady = false;
            if (package == null) yield break;
            yield return package.DestroyAsync();
            YooAssets.RemovePackage(package); package = null;
        }
        /// <summary>开发源加载不代表 YooAsset 包可用，仅允许编辑器读取权威配置。</summary>
        public static byte[] LoadBytesForEditorSource(string address)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(address) || Path.GetFileName(address) != address || !address.EndsWith(".bytes", StringComparison.Ordinal))
                throw new ArgumentException("必须使用含 .bytes 扩展名的配置文件名。", nameof(address));
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + address);
            if (asset == null) throw new FileNotFoundException("开发源配置不存在", address);
            return asset.bytes;
#else
            throw new PlatformNotSupportedException("源开发模式仅限编辑器。");
#endif
        }
        public static bool IsDevelopmentSource => Application.isEditor;
    }
}
