using System.Collections;
using System.Linq;
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
    /// <summary>验证 Boot 实际入口的接受攻击、冷却与空仓、持枪装填和暂停，限定单一集成用例。</summary>
    public sealed class PrototypeCombatVisualTests
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
        public IEnumerator BootCombatShowsAcceptedAttacksAndReload()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            PrototypeController owner = null;
            for (int i=0;i<600 && owner==null;i++) { owner=Object.FindObjectOfType<PrototypeController>();yield return null; }
            Assert.NotNull(owner);
            for(int i=0;i<10;i++)yield return null;
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/PlayerCombat-20261119"));
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
            var combat=actor.GetComponent<PrototypeCombatVisual>();Assert.NotNull(combat);
            run.Survival.weapon=new SurvivalStack(SurvivalItem.Crowbar);
            int version=run.SurvivalAttackVersion;
            var aim=run.Player+Vector2.up*4;
            run.SurvivalAttack(aim,-1);
            Assert.AreEqual(version+1,run.SurvivalAttackVersion);
            run.SurvivalAttack(aim,-1);
            Assert.AreEqual(version+1,run.SurvivalAttackVersion,"冷却中的攻击不能再次触发动画");
            yield return null;yield return null;
            Assert.That(animator.GetLayerWeight(1),Is.GreaterThan(.9f));
            var right=animator.GetBoneTransform(HumanBodyBones.RightHand);
            for(int i=0;i<12;i++) {yield return new WaitForSecondsRealtime(.065f);CloseCapture(owner,actor,Path.Combine(output,"melee-"+i.ToString("D2")+".png"));}
            run.Survival.weapon=new SurvivalStack(SurvivalItem.Pistol){rounds=2};
            run.Survival.attackCooldown=0;
            yield return null;yield return null;
            Assert.IsTrue(actor.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(1).IsName("Aim") || animator.IsInTransition(1));
            Assert.IsTrue(run.SurvivalAttack(aim,-1));
            Assert.AreEqual(1,run.Survival.weapon.rounds);
            yield return null;yield return null;
            Assert.IsTrue(actor.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="枪口闪光" && t.gameObject.activeInHierarchy));
            CloseCapture(owner,actor,Path.Combine(output,"shoot.png"));
            for(int i=0;i<8;i++){yield return new WaitForSecondsRealtime(.03f);CloseCapture(owner,actor,Path.Combine(output,"shoot-"+i.ToString("D2")+".png"));}
            run.Survival.weapon.rounds=0;run.Survival.attackCooldown=0;
            version=run.SurvivalAttackVersion;run.SurvivalAttack(aim,-1);
            Assert.AreEqual(version,run.SurvivalAttackVersion,"空仓不能播放射击或闪光");
            run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Ammo){count=4});
            Assert.IsTrue(run.BeginSurvivalAction(SurvivalAction.Reload));
            for(int i=0;i<12;i++){yield return new WaitForSecondsRealtime(.19f);CloseCapture(owner,actor,Path.Combine(output,"reload-"+i.ToString("D2")+".png"));}
            type.GetField("scavengePaused",Hidden).SetValue(owner,true);
            yield return null;yield return null;
            var pausedPose=right.localRotation;
            for(int i=0;i<10;i++)yield return null;
            Assert.That(Quaternion.Angle(pausedPose,right.localRotation),Is.LessThan(.1f));
            type.GetField("scavengePaused",Hidden).SetValue(owner,false);
            yield return new ExitPlayMode();
        }

        private static void CloseCapture(PrototypeController owner,Transform actor,string path)
        {
            var camera=(Camera)typeof(PrototypeController).GetField("gameCamera",Hidden).GetValue(owner);
            var position=camera.transform.position;var rotation=camera.transform.rotation;var fov=camera.fieldOfView;
            camera.transform.position=actor.position+new Vector3(2,2,3.5f);camera.transform.LookAt(actor.position+Vector3.up*.95f);camera.fieldOfView=32;
            Capture(owner,path);camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;
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
