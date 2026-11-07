using NUnit.Framework;
using OutpostLoop.Resources.Editor;
namespace OutpostLoop.Resources.Tests
{
    /// <summary>范围仅四条：真实源收集、源配置读取及非法地址拒绝；不验证运行包。</summary>
    public sealed class ResourceFoundationTests
    {
        [Test] public void UiSourceAndCollectorUseSameLogicalAddress()
        {
            ResourceSourceAudit.EnsureSettings();
            const string path="Assets/Game/Prefabs/UI/AlignmentProbe.prefab";
            var root=new UnityEngine.GameObject("AlignmentProbe");
            try {
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(root,path);
                var setting=UnityEditor.AssetDatabase.LoadAssetAtPath<YooAsset.Editor.AssetBundleCollectorSetting>(ResourceSourceAudit.SettingPath);
                var result=setting.BeginCollect(ResourcePackageRuntime.PackageName,true,false);
                var entry=System.Linq.Enumerable.Single(result.CollectAssets,x=>x.Address=="Prefabs/UI/AlignmentProbe");
                Assert.AreEqual(path,entry.AssetInfo.AssetPath);CollectionAssert.Contains(entry.AssetTags,"startup");
                Assert.NotNull(new EditorSourceResourceLoader().LoadAsset<UnityEngine.GameObject>(entry.Address));
            } finally { UnityEngine.Object.DestroyImmediate(root);UnityEditor.AssetDatabase.DeleteAsset(path); }
        }
        [Test] public void ActualCollectorContainsStartupRule() { ResourceSourceAudit.Audit(); }
        [Test] public void EditorSourceReadsRealRule() { Assert.IsNotEmpty(new EditorSourceResourceLoader().LoadAsset<UnityEngine.TextAsset>("tbrule.bytes").bytes); }
        [Test] public void EditorSourceRejectsTraversal() { Assert.Throws<System.ArgumentException>(() => new EditorSourceResourceLoader().LoadAsset<UnityEngine.TextAsset>("../tbrule.bytes")); }
    }
}
