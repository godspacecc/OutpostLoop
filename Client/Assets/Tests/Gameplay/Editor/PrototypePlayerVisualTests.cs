using System.Collections;
using System.IO;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace OutpostLoop.Tests
{
    /// <summary>验证 Boot 实际入口的人形资源、输入移动、暂停及续玩重建，限定单一集成用例。</summary>
    public sealed class PrototypePlayerVisualTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Keyboard keyboard;

        /// <summary>失败时也释放虚拟输入，避免影响编辑器后续试玩。</summary>
        [TearDown]
        public void ReleaseKeyboard()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        }

        [UnityTest]
        public IEnumerator BootPlayerMovesAnimatesFreezesAndResumes()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            PrototypeController owner = null;
            for (int i=0;i<600 && owner==null;i++) { owner=Object.FindObjectOfType<PrototypeController>();yield return null; }
            Assert.NotNull(owner);
            for(int i=0;i<10;i++)yield return null;
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/PrototypePack-20261118"));
            var type=typeof(PrototypeController);
            type.GetField("survivalStore",Hidden).SetValue(owner,new JsonFileStore<SurvivalSave>(Path.Combine(output,"Save-"+System.Guid.NewGuid().ToString("N"))));
            type.GetField("survivalSaveChecked",Hidden).SetValue(owner,false);
            Call(owner,"RefreshOverlay");
            Click("StartSurvival");
            for(int i=0;i<600 && owner.IsCrossing;i++)yield return null;
            Assert.IsFalse(owner.IsCrossing);
            var run=(ScavengeRun)type.GetField("scavengeRun",Hidden).GetValue(owner);
            Assert.NotNull(run);
            var actor=(Transform)type.GetField("scavengePlayer",Hidden).GetValue(owner);
            Assert.NotNull(actor.GetComponent<PrototypePlayerVisual>());
            var animator=actor.GetComponentInChildren<Animator>();
            Assert.IsTrue(animator.avatar.isValid && animator.avatar.isHuman);
            Assert.IsFalse(animator.applyRootMotion);
            Assert.IsEmpty(actor.GetComponentsInChildren<Collider>(),"模型不能另外增加与领域不一致的碰撞");
            foreach(var renderer in actor.GetComponentsInChildren<Renderer>())
                foreach(var material in renderer.sharedMaterials)Assert.AreEqual("Standard",material.shader.name);
            keyboard=InputSystem.AddDevice<Keyboard>();
            var start=run.Player;
            for(int i=0;i<40;i++) { InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));yield return null; }
            Assert.That(Vector2.Distance(start,run.Player),Is.GreaterThan(.05f),"注入持续行走应改变领域位置");
            Assert.That(animator.GetFloat("Speed"),Is.GreaterThan(.1f));
            Assert.That(Vector2.Distance(new Vector2(actor.position.x,actor.position.z),run.Player),Is.LessThan(.01f));
            for(int i=0;i<35;i++) { InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D,Key.LeftShift));yield return null; }
            Assert.IsTrue(run.SurvivalSprint);
            Assert.That(animator.GetFloat("Speed"),Is.GreaterThan(1.5f),"冲刺应进入跑步混合区间");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            for(int i=0;i<20;i++)yield return null;
            type.GetField("scavengePaused",Hidden).SetValue(owner,true);
            yield return null;yield return null;
            double clock=run.Survival.elapsed;
            float phase=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            var position=run.Player;
            for(int i=0;i<15;i++)yield return null;
            Assert.AreEqual(clock,run.Survival.elapsed);
            Assert.AreEqual(position,run.Player);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime,Is.EqualTo(phase).Within(.0001f));
            type.GetField("scavengePaused",Hidden).SetValue(owner,false);
            for(int i=0;i<10;i++)yield return null;
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime,Is.GreaterThan(phase));
            var regionMapOpen = type.GetField("survivalRegionMapOpen", Hidden);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.M));
            yield return null;
            yield return null;
            Assert.IsTrue((bool)regionMapOpen.GetValue(owner), "M 应打开区域地图");
            Assert.AreEqual(0, animator.speed, "区域地图应冻结动画");
            double regionClock = run.Survival.elapsed;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
            Assert.AreEqual(regionClock, run.Survival.elapsed, "区域地图应暂停局时");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.M));
            yield return null;
            yield return null;
            Assert.IsFalse((bool)regionMapOpen.GetValue(owner), "再按 M 应返回行动");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            for(int i=0;i<5;i++)yield return null;
            Capture(owner,Path.Combine(output,"main-verified.png"));
            Call(owner,"SaveAndLeaveSurvival");
            Click("ContinueSurvival");
            for(int i=0;i<600 && owner.IsCrossing;i++)yield return null;
            var resumed=(Transform)type.GetField("scavengePlayer",Hidden).GetValue(owner);
            Assert.NotNull(resumed.GetComponent<PrototypePlayerVisual>());
            Assert.AreNotSame(actor,resumed);
            Assert.That(Vector2.Distance(new Vector2(resumed.position.x,resumed.position.z),position),Is.LessThan(.01f));
            yield return new ExitPlayMode();
        }

        private static void Call(PrototypeController owner,string method) => typeof(PrototypeController).GetMethod(method,Hidden).Invoke(owner,null);
        private static void Click(string name)
        {
            var button=GameObject.Find(name);Assert.NotNull(button,name);
            Assert.IsTrue(ExecuteEvents.Execute(button,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler));
        }

        /// <summary>保留主游戏相机画面，不用独立预览冒充 Boot 验收。</summary>
        private static void Capture(PrototypeController owner,string path)
        {
            var camera=(Camera)typeof(PrototypeController).GetField("gameCamera",Hidden).GetValue(owner);
            var target=new RenderTexture(1280,720,24);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
            File.WriteAllBytes(path,texture.EncodeToPNG());RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(texture);Object.DestroyImmediate(target);
        }
    }
}
