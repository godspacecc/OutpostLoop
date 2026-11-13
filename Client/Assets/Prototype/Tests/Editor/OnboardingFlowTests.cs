using System.Collections;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OutpostLoop.Tests
{
    /// <summary>从真实 Boot 验证初次求生、续玩和首次回归的表现顺序。</summary>
    public sealed class OnboardingFlowTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator NewRunResumeAndReturnUseTheirOwnTransitions()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            PrototypeController controller = null;
            for (int frame = 0; frame < 600 && controller == null; frame++)
            {
                controller = Object.FindObjectOfType<PrototypeController>();
                yield return null;
            }
            Assert.NotNull(controller);
            for (int frame = 0; frame < 10; frame++) yield return null;
            var type = typeof(PrototypeController);
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/OnboardingFlow-20261113/Save-" + System.Guid.NewGuid().ToString("N")));
            type.GetField("survivalStore", Hidden).SetValue(controller, new JsonFileStore<SurvivalSave>(directory));
            type.GetField("survivalSaveChecked", Hidden).SetValue(controller, false);
            Call(controller, "RefreshOverlay");
            Assert.IsNull(GameObject.Find("CosmicSpace"), "新档尚未见过空间");
            Click("StartSurvival");
            Assert.IsTrue(controller.IsCrossing);
            Assert.IsNull(GameObject.Find("CosmicCrossing"));
            yield return WaitForLoading(controller);
            var run = Run(controller);
            Assert.That(Call(controller, "SurvivalObjective"), Does.Contain("WASD"));
            run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Water));
            Assert.That(Call(controller, "SurvivalObjective"), Does.Contain("Tab"));
            run.Survival.elapsed = 1600;
            run.Survival.water = 43;
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, new Vector2(-60, -50));
            var savedPosition = run.Player;
            Call(controller, "SaveAndLeaveSurvival");
            Assert.IsTrue(controller.HasSurvivalSave);
            Assert.IsNull(GameObject.Find("CosmicSpace"));
            Click("RestartSurvival");
            Assert.IsFalse(controller.IsCrossing, "确认之前不能创建新行动");
            Assert.IsNull(Run(controller));
            Click("CancelRestartSurvival");
            Assert.IsTrue(controller.HasSurvivalSave);
            Click("ContinueSurvival");
            Assert.IsNull(GameObject.Find("CosmicCrossing"));
            Assert.AreEqual("正在恢复行动", GameObject.Find("CrossingStatus").GetComponent<Text>().text);
            yield return WaitForLoading(controller);
            var resumed = Run(controller);
            Assert.AreNotSame(run, resumed);
            Assert.That(resumed.Survival.elapsed, Is.InRange(1600, 1602));
            Assert.That(resumed.Survival.water, Is.InRange(42, 43));
            Assert.AreEqual(savedPosition, resumed.Player);
            Assert.AreEqual(1, resumed.Survival.pack.Count);
            resumed.Survival.elapsed = ScavengeSurvival.Goal;
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(resumed, ScavengeTown.Exit);
            resumed.Tick(.01f, Vector2.zero);
            Assert.IsTrue(resumed.SurvivalExit());
            Call(controller, "ReturnFromSurvival");
            Assert.IsTrue(controller.JourneyProgress.visitedSpace);
            Assert.NotNull(GameObject.Find("CosmicSpace"));
            Assert.IsFalse(controller.HasSurvivalSave);
            Click("StartSurvival");
            Assert.NotNull(GameObject.Find("CosmicCrossing"), "空间主动出发使用穿越过渡");
            yield return WaitForLoading(controller);
            Assert.IsEmpty(Run(controller).Survival.pack);
            Call(controller, "SaveAndLeaveSurvival");
            Click("RestartSurvival");
            Assert.IsFalse(controller.IsCrossing);
            Click("ConfirmRestartSurvival");
            Assert.IsTrue(controller.IsCrossing);
            Assert.IsFalse(controller.HasSurvivalSave);
            yield return WaitForLoading(controller);
            Assert.That(Run(controller).Survival.elapsed, Is.LessThan(2));
            Assert.IsTrue(controller.JourneyProgress.completedFirst);
            yield return new ExitPlayMode();
        }

        private static object Call(PrototypeController controller, string name) =>
            typeof(PrototypeController).GetMethod(name, Hidden).Invoke(controller, null);

        private static ScavengeRun Run(PrototypeController controller) =>
            (ScavengeRun)typeof(PrototypeController).GetField("scavengeRun", Hidden).GetValue(controller);

        private static void Click(string name)
        {
            var target = GameObject.Find(name);
            Assert.NotNull(target, name);
            ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current)
                { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }

        private static IEnumerator WaitForLoading(PrototypeController controller)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (controller.IsCrossing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(controller.IsCrossing);
        }
    }
}
