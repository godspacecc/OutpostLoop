using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
                    var buttons = GameObject.Find("StartLayout").GetComponentsInChildren<Button>();
                    var names = new[] { hasSave ? "ContinueSurvival" : "StartSurvival", "SettingsEntry", "QuitGame" };
                    CollectionAssert.AreEquivalent(names, System.Array.ConvertAll(buttons, button => button.name));
                    Assert.IsNull(GameObject.Find("StartLayout").transform.Find("TrialDestinations"));
                    // 检查可见按钮之间的实际空隙，覆盖无档→有档→无档的重排。
                    for(int i=1;i<names.Length;i++)
                    {
                        var previous=GameObject.Find(names[i-1]).GetComponent<RectTransform>();
                        var current=GameObject.Find(names[i]).GetComponent<RectTransform>();
                        float gap=previous.anchoredPosition.y-current.anchoredPosition.y-(previous.rect.height+current.rect.height)/2;
                        Assert.That(gap,Is.InRange(12f,24f),"可见菜单按钮之间不应留下隐藏入口的空行："+names[i]);
                    }
                    var directory=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Backup/Artifacts/RiverValleyOnlyMenu-20261119"));
                    System.IO.Directory.CreateDirectory(directory);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory, hasSave ? "with-save.png" : "without-save.png"));
                    for (int frame = 0; frame < 8; frame++) yield return null;
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
