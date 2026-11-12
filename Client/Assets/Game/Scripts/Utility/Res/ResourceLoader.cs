using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>
    /// 项目静态资源入口，默认转发到 YooAsset 实现，测试可替换加载器并复位。
    /// </summary>
    public static class ResourceLoader
    {
        private static readonly IResourceLoader DefaultLoader = new NullResourceLoader();
        private static IResourceLoader _current = DefaultLoader;
        private static readonly List<IDisposable> SessionLoaders = new List<IDisposable>();

        public static IResourceLoader Current => _current;

        public static bool IsConfigured => !ReferenceEquals(_current, DefaultLoader);

        /// <summary>接管可释放加载器的会话所有权；切换时保留旧资源，直到使用方退出后调用 Shutdown。</summary>
        public static void Use(IResourceLoader loader)
        {
            _current = loader ?? DefaultLoader;
            if (loader is IDisposable disposable && !SessionLoaders.Exists(item => ReferenceEquals(item, disposable)))
                SessionLoaders.Add(disposable);
        }

        public static T LoadAsset<T>(string path) where T : Object
        {
            return _current.LoadAsset<T>(path);
        }

        public static string LoadText(string path)
        {
            return LoadAsset<TextAsset>(path)?.text;
        }

        public static ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
        {
            var owner = _current;
            var handle = owner.LoadAssetAsync(path, onLoaded);
            return new ResourceLoadHandle(handle.RawHandle, owner);
        }

        public static void Release(ResourceLoadHandle handle)
        {
            (handle.Owner ?? _current).Release(handle);
        }

        public static void SetLoaderForTests(IResourceLoader loader)
        {
            Use(loader);
        }

        public static void ResetForTests() => Shutdown();

        /// <summary>业务停止使用资源后关闭所有会话加载器；单个清理失败不阻断其他加载器，重复调用无副作用。</summary>
        public static void Shutdown()
        {
            _current = DefaultLoader;
            var loaders = SessionLoaders.ToArray();
            SessionLoaders.Clear();
            foreach (var loader in loaders)
            {
                try { loader.Dispose(); }
                catch (Exception error) { Log.Error($"[ResourceLoader] 资源会话释放失败：{error}"); }
            }
        }

        private sealed class NullResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                Log.Error($"[ResourceLoader] 资源加载失败：DefaultPackage 为空，资源地址：{path}");
                return null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                Log.Error($"[ResourceLoader] 异步资源加载失败：DefaultPackage 为空，资源地址：{path}");
                onLoaded?.Invoke(null);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}
