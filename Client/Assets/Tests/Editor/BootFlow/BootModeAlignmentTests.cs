using System;
using System.Collections;
using System.Linq;
using Boot;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Game.Tests.Flow
{
    /// <summary>五项限定检查，防止本项目重新引入绕过标准资源包的 Boot 模式。</summary>
    public sealed class BootModeAlignmentTests
    {
        [Test] public void WindowsIdentityMatchesLocalLedger()
        {
            var ledger=JsonUtility.FromJson<WindowsUpdateManifest>(System.IO.File.ReadAllText("Assets/Editor/Build/windows-release-version.json"));
            Assert.AreEqual("com.outpostloop.game",WindowsUpdateManifest.ProductId);
            Assert.AreEqual(WindowsUpdateManifest.ProductId,ledger.productId);
        }
        [Test] public void PlayModesMatchReferenceContract()
        {
            CollectionAssert.AreEqual(new[]{"EditorSimulateMode","OfflinePlayMode","HostPlayMode"},Enum.GetNames(typeof(ResourcePlayMode)));
        }
        [Test] public void StartupSourcesMatchReferenceContract()
        {
            CollectionAssert.AreEqual(new[]{"EditorSimulate","HostLatest","HostCached","BuiltinFallback"},Enum.GetNames(typeof(BootResourceSource)));
        }
        [Test] public void NewResourceBootDefaultsToSimulation()
        {
            var root=new GameObject("BootModeProbe");
            try { var boot=root.AddComponent<ResourceBoot>();Assert.AreEqual(0,new SerializedObject(boot).FindProperty("_editorPlayMode").enumValueIndex); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test] public void SavedBootSceneUsesSimulation()
        {
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity",OpenSceneMode.Additive);
            try {
                var boot=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ResourceBoot>(true)).Single();
                Assert.AreEqual(0,new SerializedObject(boot).FindProperty("_editorPlayMode").intValue);
            } finally {EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
        }
    }
}
