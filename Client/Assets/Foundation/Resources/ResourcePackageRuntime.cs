using System;
using System.Collections;
using System.IO;
using UnityEngine;
using YooAsset;
using Game;
namespace OutpostLoop.Resources
{
    /// <summary>独立资源包入口；只读取已有清单，绝不生成模拟包。</summary>
    public sealed class ResourcePackageRuntime
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
            ResourceLoader.Use(new YooAssetResourceLoader(package));
            IsReady = true;
        }
        /// <summary>调用方须先释放全部租约，再等待关闭完成。</summary>
        public IEnumerator Shutdown()
        {
            IsReady = false;
            ResourceLoader.Use(null);
            if (package == null) yield break;
            yield return package.DestroyAsync();
            YooAssets.RemovePackage(package); package = null;
        }
    }
}
