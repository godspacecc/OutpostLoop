using System;
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

        public static IResourceLoader Current => _current;

        public static bool IsConfigured => !ReferenceEquals(_current, DefaultLoader);

        public static void Use(IResourceLoader loader)
        {
            _current = loader ?? DefaultLoader;
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
            return _current.LoadAssetAsync(path, onLoaded);
        }

        public static void Release(ResourceLoadHandle handle)
        {
            _current.Release(handle);
        }

        public static void SetLoaderForTests(IResourceLoader loader)
        {
            Use(loader);
        }

        public static void ResetForTests()
        {
            _current = DefaultLoader;
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
