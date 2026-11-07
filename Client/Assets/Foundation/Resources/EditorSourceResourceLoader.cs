using System;
using System.IO;
using Game;
using UnityEngine;
using Object = UnityEngine.Object;
namespace OutpostLoop.Resources
{
    /// <summary>编辑器源后端实现公共加载契约；不生成清单，不代表运行包验证。</summary>
    public sealed class EditorSourceResourceLoader : IResourceLoader
    {
        /// <summary>仅接受配置文件名或 Game 下不带扩展名的 UI 逻辑地址。</summary>
        public T LoadAsset<T>(string address) where T : Object
        {
#if UNITY_EDITOR
            string path;
            if (string.IsNullOrWhiteSpace(address) || address.Contains("..") || address.Contains(":") || address.Contains("\\"))
                throw new ArgumentException("非法资源地址", nameof(address));
            if (Path.GetFileName(address)==address && address.EndsWith(".bytes",StringComparison.Ordinal))
                path="Assets/Game/Config/"+address;
            else if (address == "languages.json")
                path="Assets/Game/Config/Localization/languages.json";
            else if (address.StartsWith("Prefabs/UI/",StringComparison.Ordinal) &&
                ResourceAddress.TryFromRelativePath(ResourceAddressKind.UiPrefab,address.Substring("Prefabs/UI/".Length),out var resolved,out _))
                path="Assets/Game/"+resolved+".prefab";
            else throw new ArgumentException("未配置的源资源地址",nameof(address));
            var asset=UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            if(asset==null) Log.Error("[EditorSourceResourceLoader] 源资源不存在："+address);
            return asset;
#else
            throw new PlatformNotSupportedException("源资源后端仅限编辑器。");
#endif
        }
        /// <summary>源加载立即完成，没有需要释放的 YooAsset 句柄。</summary>
        public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> onLoaded) where T:Object
        { onLoaded?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
        /// <summary>源对象由 AssetDatabase 管理，不卸载公共资源。</summary>
        public void Release(ResourceLoadHandle handle) { }
    }
}
