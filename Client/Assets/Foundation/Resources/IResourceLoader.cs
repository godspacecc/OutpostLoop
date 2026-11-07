using System;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>
    /// 定义资源加载和释放边界，约定异步加载返回的 Handle 由调用方在不再使用时释放。
    /// </summary>
    public interface IResourceLoader
    {
        T LoadAsset<T>(string path) where T : Object;
        ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object;
        void Release(ResourceLoadHandle handle);
    }
}
