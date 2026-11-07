using System;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>
    /// 基于 YooAsset Package 加载资源，失败时记录项目上下文并将底层错误原文保留在日志中。
    /// </summary>
    public sealed class YooAssetResourceLoader : IResourceLoader
    {
        private readonly ResourcePackage _package;

        public YooAssetResourceLoader(ResourcePackage package)
        {
            _package = package;
        }

        public T LoadAsset<T>(string path) where T : Object
        {
            if (_package == null)
            {
                Log.Error($"[YooAssetResourceLoader] 资源加载失败：DefaultPackage 为空，资源地址：{path}");
                return null;
            }

            var handle = _package.LoadAssetSync<T>(path);
            if (handle.Status == EOperationStatus.Succeed)
            {
                return handle.AssetObject as T;
            }

            Log.Error($"[YooAssetResourceLoader] 资源加载失败：{path}，底层错误：{handle.LastError}");
            handle.Release();
            return null;
        }

        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
        {
            if (_package == null)
            {
                Log.Error($"[YooAssetResourceLoader] 异步资源加载失败：DefaultPackage 为空，资源地址：{path}");
                onLoaded?.Invoke(null);
                return ResourceLoadHandle.Empty;
            }

            var handle = _package.LoadAssetAsync<T>(path);
            handle.Completed += operation =>
            {
                if (operation.Status == EOperationStatus.Succeed)
                {
                    onLoaded?.Invoke(operation.AssetObject as T);
                    return;
                }

                Log.Error($"[YooAssetResourceLoader] 异步资源加载失败：{path}，底层错误：{operation.LastError}");
                onLoaded?.Invoke(null);
            };

            return new ResourceLoadHandle(handle);
        }

        public void Release(ResourceLoadHandle handle)
        {
            if (handle.RawHandle is AssetHandle assetHandle)
            {
                assetHandle.Release();
            }
        }
    }
}
