using System;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>
    /// 同步资源借用至加载器会话关闭；可释放实现通过 IDisposable 归还会话资源。
    /// 异步加载返回的 Handle 由调用方在不再使用时释放。
    /// </summary>
    public interface IResourceLoader
    {
        T LoadAsset<T>(string path) where T : Object;
        ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object;
        void Release(ResourceLoadHandle handle);
    }
}
