using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace OutpostLoop.Tests
{
    /// <summary>验证同一开始页随续玩状态切换入口，避免同时展示两趟行动。</summary>
    public sealed class SingleActionMenuTests
    {
        [UnityTest]
        public IEnumerator BootMenuShowsOneActionEntry()
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
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(PrototypeController);
            var checkedField = type.GetField("survivalSaveChecked", flags);
            var saveField = type.GetField("survivalHasSave", flags);
            bool wasChecked = (bool)checkedField.GetValue(controller);
            bool hadSave = (bool)saveField.GetValue(controller);
            try
            {
                // 只切换缓存快照，避免覆盖玩家磁盘上的行动。
                checkedField.SetValue(controller, true);
                foreach (bool hasSave in new[] { false, true, false })
                {
                    saveField.SetValue(controller, hasSave);
                    type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
                    Assert.AreEqual(!hasSave, GameObject.Find("StartSurvival") != null);
                    Assert.AreEqual(hasSave, GameObject.Find("ContinueSurvival") != null);
                    Assert.AreEqual(hasSave, GameObject.Find("RestartSurvival") != null);
                }
            }
            finally
            {
                checkedField.SetValue(controller, wasChecked);
                saveField.SetValue(controller, hadSave);
            }
            yield return new ExitPlayMode();
        }
    }
}
