using System;
using System.IO;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace OutpostLoop.Tests
{
    /// <summary>在仓库产物目录生成真实视图源码，验证生成路径及组件引用转换。</summary>
    public sealed class UIGeneratorFoundationTests
    {
        /// <summary>真实生成源码作为测试夹具编译，再用正式绑定器保存并读回引用。</summary>
        [Test]
        public void CompiledGeneratedViewBindsSerializedReference()
        {
            const string path = "Assets/Tests/EditMode/UIGenerator/TemporaryBindingProbe.prefab";
            var root = new GameObject("FoundationGeneratedProbe", typeof(RectTransform));
            try
            {
                root.AddComponent<Button>();
                var collector = root.AddComponent<ReferenceCollector>();
                collector.data.Add(new ReferenceCollectorData { key = "ActionButton", gameObject = root, TypeName = typeof(Button).FullName });
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Invoke("BindGeneratedUiComponent", path, typeof(FoundationGeneratedProbeUI).FullName);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var view = prefab.GetComponent<FoundationGeneratedProbeUI>();
                Assert.NotNull(view);
                Assert.AreSame(prefab.GetComponent<Button>(), view.ActionButton);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); AssetDatabase.DeleteAsset(path); }
        }

        [Test]
        public void GeneratorWritesViewAndMapsComponentReference()
        {
            var root = new GameObject("FoundationGeneratedProbe", typeof(RectTransform));
            try
            {
                var button = root.AddComponent<Button>();
                var collector = root.AddComponent<ReferenceCollector>();
                collector.data.Add(new ReferenceCollectorData { key = "ActionButton", gameObject = root, TypeName = typeof(Button).FullName });
                var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backup/Artifacts/FrameworkAlignment-20261107/GeneratedUI/FoundationGeneratedProbeUI.cs"));
                Invoke("WriteViewScript", root, output, collector, "UI", "BaseUI");
                var source = File.ReadAllText(output);
                StringAssert.Contains("class FoundationGeneratedProbeUI : BaseUI", source);
                StringAssert.Contains("public Button ActionButton;", source);
                StringAssert.Contains("using UnityEngine.UI;", source);
                Assert.AreEqual("Prefabs/UI/Probe/FoundationGeneratedProbe", Invoke("GetUiPrefabResourcePath", "Assets/Game/Prefabs/UI/Probe/FoundationGeneratedProbe.prefab"));
                Assert.AreEqual("Assets/Game/Scripts/Controller/UI/Probe/FoundationGeneratedProbeUI.cs", Invoke("GetGeneratedScriptPath", "Assets/Game/Prefabs/UI/Probe/FoundationGeneratedProbe.prefab", "Assets/Game/Scripts/Controller/UI/", "UI"));
                Assert.AreSame(button, Invoke("ResolveReferenceObject", root, typeof(Button)));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        /// <summary>同一视图有多种触屏控件时只生成一次命名空间引用。</summary>
        [Test]
        public void GeneratorImportsOnScreenNamespaceForTouchControls()
        {
            var root = new GameObject("TouchProbe", typeof(RectTransform));
            try
            {
                var collector = root.AddComponent<ReferenceCollector>();
                collector.data.Add(new ReferenceCollectorData { key = "Stick", gameObject = root, TypeName = "UnityEngine.InputSystem.OnScreen.OnScreenStick" });
                collector.data.Add(new ReferenceCollectorData { key = "Button", gameObject = root, TypeName = "UnityEngine.InputSystem.OnScreen.OnScreenButton" });
                var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backup/Artifacts/FoundationLifecycleParity-20261110/TouchProbeUI.cs"));
                Invoke("WriteViewScript", root, output, collector, "UI", "BaseUI");
                var source = File.ReadAllText(output);
                const string import = "using UnityEngine.InputSystem.OnScreen;";
                Assert.That(source.Split(new[] { import }, StringSplitOptions.None).Length, Is.EqualTo(2));
                StringAssert.Contains("public OnScreenStick Stick;", source);
                StringAssert.Contains("public OnScreenButton Button;", source);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>同名 Prefab 在三个项目间应生成同一类名和引用类型。</summary>
        [Test] public void BoxNamingAndCollectorDefaultMatchReference()
        {
            var method=typeof(BoxUICodeSpawner).GetMethod("GetBoxUiScriptAssetPath",BindingFlags.Static|BindingFlags.NonPublic);
            Assert.AreEqual("Assets/Game/Scripts/Controller/UI/Test/Box/ItemBoxBoxUI.cs",method.Invoke(null,new object[]{"Assets/Game/Prefabs/UI/Test/Box/ItemBox.prefab"}));
            var root=new GameObject("reference probe",typeof(RectTransform));
            try {var collector=root.AddComponent<ReferenceCollector>();collector.Add("root",root);
                Assert.AreEqual(typeof(RectTransform).FullName,collector.data[0].TypeName);
            } finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static object Invoke(string method, params object[] args)
        {
            return typeof(UICodeSpawner).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        }
    }
}
