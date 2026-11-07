using System;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>
    /// 基于 YooAsset Package 加载资源，失败时记录项目上下文并将底层错误原文保留在日志中。
    /// </summary>
    public sealed class YooAssetResourceLoader : IResourceLoader, IDisposable
    {
        private readonly ResourcePackage _package;
        // 同步返回值借用此会话持有的资源，直到 Dispose 才归还句柄。
        private readonly Dictionary<(string, Type), AssetHandle> syncHandles = new Dictionary<(string, Type), AssetHandle>();
        private bool disposed;

        public YooAssetResourceLoader(ResourcePackage package)
        {
            _package = package;
        }

        public T LoadAsset<T>(string path) where T : Object
        {
            ThrowIfDisposed();
            if (_package == null)
            {
                Log.Error($"[YooAssetResourceLoader] 资源加载失败：DefaultPackage 为空，资源地址：{path}");
                return null;
            }

            var key = (path, typeof(T));
            if (syncHandles.TryGetValue(key, out var cached))
            {
                if (cached.IsValid && cached.AssetObject is T asset) return asset;
                syncHandles.Remove(key);
                if (cached.IsValid) cached.Release();
            }
            var handle = _package.LoadAssetSync<T>(path);
            if (handle.Status == EOperationStatus.Succeed && handle.AssetObject is T loaded)
            {
                syncHandles.Add(key, handle);
                return loaded;
            }

            Log.Error($"[YooAssetResourceLoader] 资源加载失败：{path}，底层错误：{handle.LastError}");
            handle.Release();
            return null;
        }

        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
        {
            ThrowIfDisposed();
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

        /// <summary>使用方停止使用同步资源后关闭会话；异步句柄仍由调用方归还，关闭后也可 Release。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var handle in syncHandles.Values)
            {
                try { if (handle.IsValid) handle.Release(); }
                catch (Exception error) { Log.Error($"[YooAssetResourceLoader] 同步句柄释放失败：{error}"); }
            }
            syncHandles.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(YooAssetResourceLoader), "资源加载器会话已关闭，不能继续加载。");
        }
    }
}
