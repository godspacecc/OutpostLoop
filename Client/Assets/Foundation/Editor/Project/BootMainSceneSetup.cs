using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using YooAsset.Editor;
using Boot;
using Boot.UI;
namespace OutpostLoop.Editor
{
    /// <summary>创建独立临时启动预览并拆分场景；不构建、不发布、不导入旧视觉。</summary>
    public static class BootMainSceneSetup
    {
        /// <summary>宿主场景路径，唯一随 Player 打包的入口。</summary>
        public const string BootPath="Assets/Game/Scenes/Boot.unity";
        /// <summary>主场景路径，以 Scenes/Main 地址加入资源包。</summary>
        public const string MainPath="Assets/Game/Scenes/Main.unity";
        [MenuItem("Tools/开发/初始化Boot与Main")]
        /// <summary>生成临时启动场景并配置独立主场景；不会执行构建。</summary>
        public static void Prepare()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("请先退出Play。");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("场景有未保存修改，请先保存后再拆分。");
            Directory.CreateDirectory("Assets/Game/Scenes");Directory.CreateDirectory("Assets/Game/Dlls");Directory.CreateDirectory("Assets/StreamingAssets");AssetDatabase.Refresh();
            if(!File.Exists(MainPath))
            {
                var error=AssetDatabase.MoveAsset("Assets/Prototype/Scenes/Boot.unity",MainPath);
                if(!string.IsNullOrEmpty(error))throw new InvalidOperationException(error);
            }
            var main=EditorSceneManager.OpenScene(MainPath);
            foreach(var loop in UnityEngine.Object.FindObjectsOfType<Game.FoundationLoop>())UnityEngine.Object.DestroyImmediate(loop);
            // 基础根独立于玩法入口，重载 Main 时由既有实例继续驱动。
            var foundation=GameObject.Find("FoundationRoot")??new GameObject("FoundationRoot");
            var driver=foundation.AddComponent<Game.FoundationLoop>();SetBool(driver,"persistAcrossScenes",true);
            EditorSceneManager.SaveScene(main);
            // 启动界面使用本项目临时 UGUI 几何，不引用参考项目美术。
            var boot=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var canvasObject=new GameObject("TemporaryBootCanvas",typeof(RectTransform));canvasObject.SetActive(false);
            var canvas=canvasObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.overrideSorting=true;canvas.sortingOrder=1000;
            var scaler=canvasObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            canvasObject.AddComponent<GraphicRaycaster>();canvasObject.AddComponent<TemporaryBootFont>();
            var panel=Child(canvasObject,"TemporaryLoadingPanel");panel.AddComponent<Image>().color=new Color(.08f,.11f,.13f,1);
            var view=panel.AddComponent<HotUpdateView>();
            string[] fields={"_statusText","_versionText","_titleText","_guidanceText","_detailsText"};
            for(int i=0;i<fields.Length;i++)Set(view,fields[i],Text(panel,fields[i],30,35+i*55,1180,i==4?230:50));
            // 文本和进度条必须位于对应区域内，状态切换才能一起显隐。
            var progress=Child(panel,"Progress");Set(view,"_progressArea",progress);
            var sliderObject=Child(progress,"Slider");Rect(sliderObject,30,490,1180,18);
            sliderObject.AddComponent<Image>().color=new Color(.15f,.2f,.23f,1);
            var slider=sliderObject.AddComponent<Slider>();slider.interactable=false;
            var fill=Child(sliderObject,"Fill");fill.AddComponent<Image>().color=new Color(.35f,.75f,.65f,1);
            slider.fillRect=(RectTransform)fill.transform;Set(view,"_progressSlider",slider);
            Set(view,"_progressText",Text(progress,"ProgressText",30,450,1180,36));
            var stats=Child(panel,"DownloadStats");Set(view,"_downloadStatsArea",stats);
            Set(view,"_sizeText",Text(stats,"Size",30,530,600,36));
            Set(view,"_fileCountText",Text(stats,"Files",650,530,560,36));
            Set(view,"_speedText",Text(stats,"Speed",30,570,1180,36));
            var buttons=Child(panel,"Buttons");Set(view,"_buttonArea",buttons);
            Set(view,"_retryButton",Button(buttons,"Retry","重试",30,610));Set(view,"_copyDetailsButton",Button(buttons,"Copy","复制详情",270,610));Set(view,"_quitButton",Button(buttons,"Quit","退出",510,610));
            var entry=new GameObject("BootEntry");entry.SetActive(false);var launcher=entry.AddComponent<Launcher>();Set(launcher,"_updateView",view);Set(launcher,"_updateCanvas",canvas);
            var server=panel.AddComponent<BootResourceServerView>();Set(server,"_launcher",launcher);Set(server,"_entryButton",Button(panel,"Server","资源地址",990,610));
            var editor=Child(panel,"ServerEditor");editor.AddComponent<Image>().color=new Color(.12f,.15f,.18f,1);Set(server,"_editorPanel",editor);
            Set(server,"_currentAddressText",Text(editor,"Current",40,120,1160,50));Set(server,"_feedbackText",Text(editor,"Feedback",40,230,1160,90));
            var inputObject=Child(editor,"Address");Rect(inputObject,40,190,1160,40);inputObject.AddComponent<Image>().color=Color.white;
            var input=inputObject.AddComponent<InputField>();input.textComponent=Text(inputObject,"Value",8,0,1140,40);input.textComponent.color=Color.black;Set(server,"_addressInput",input);
            Set(server,"_saveButton",Button(editor,"Save","保存并重试",40,350));Set(server,"_resetButton",Button(editor,"Reset","恢复默认",300,350));Set(server,"_cancelButton",Button(editor,"Cancel","取消",560,350));editor.SetActive(false);
            // Boot 输入根跟随 Boot 场景卸载，避免进入 Main 后出现两个事件系统。
            new GameObject("BootEventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
            canvasObject.SetActive(true);entry.SetActive(true);
            // Canvas 首次激活会重置原生排序标记，激活后再持久化覆盖设置。
            SetBool(canvas,"m_OverrideSorting",true);
            EditorSceneManager.SaveScene(boot,BootPath);EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(BootPath,true)};
            PlayerSettings.companyName="Godspace";PlayerSettings.productName="OutpostLoop";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,"com.godspace.outpostloop");PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone,"com.godspace.outpostloop");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.IL2CPP);PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,ScriptingImplementation.IL2CPP);
            var hybrid=HybridCLR.Editor.Settings.HybridCLRSettings.Instance;hybrid.hotUpdateAssemblyDefinitions=new[]{AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>("Assets/Game/Game.asmdef")};HybridCLR.Editor.Settings.HybridCLRSettings.Save();
            var host="Assets/StreamingAssets/hostConfig.json";if(!File.Exists(host))File.WriteAllText(host,"{\"resourceServer\":\"http://127.0.0.1:8000\"}");
            File.WriteAllText("Assets/Game/Dlls/.gitkeep",string.Empty);
            EnsureCollectors();AssetDatabase.SaveAssets();AssetDatabase.Refresh();
            Debug.Log("Boot/Main 已拆分；只创建独立临时启动预览，未构建资源或Player。");
        }
        /// <summary>收集Main、配置与代码；Boot留在Player宿主，不打入热更包。</summary>
        public static void EnsureCollectors()
        {
            Resources.Editor.ResourceSourceAudit.EnsureSettings();
            var setting=AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>("Assets/AssetBundleCollectorSetting.asset");var package=setting.Packages.Single(p=>p.PackageName=="OutpostLoop");
            AddGroup(package,"Main","Assets/Game/Scenes/Main.unity","AddressByGameRelativePathNoExtension","startup");
            AddGroup(package,"Code","Assets/Game/Dlls","AddressByFileNameWithExtension","startup");
            EditorUtility.SetDirty(setting);AssetDatabase.SaveAssets();
        }
        /// <summary>按组名幂等补齐资源收集声明。</summary>
        private static void AddGroup(AssetBundleCollectorPackage package,string name,string path,string rule,string tag)
        {
            if(package.Groups.Any(g=>g.GroupName==name))return;
            var group=new AssetBundleCollectorGroup{GroupName=name,ActiveRuleName="EnableGroup",AssetTags=tag};
            group.Collectors.Add(new AssetBundleCollector{CollectPath=path,CollectorGUID=AssetDatabase.AssetPathToGUID(path),CollectorType=ECollectorType.MainAssetCollector,AddressRuleName=rule,PackRuleName="PackSeparately",FilterRuleName="CollectAll"});package.Groups.Add(group);
        }
        /// <summary>建立铺满父节点的临时 UI 容器。</summary>
        private static GameObject Child(GameObject parent,string name){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent.transform,false);var r=(RectTransform)go.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;return go;}
        /// <summary>以左上角定位临时预览控件。</summary>
        private static void Rect(GameObject go,float x,float y,float w,float h){var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);}
        /// <summary>创建空白文本，内容由启动状态驱动。</summary>
        private static Text Text(GameObject parent,string name,float x,float y,float w,float h){var go=Child(parent,name);Rect(go,x,y,w,h);var text=go.AddComponent<Text>();text.font=UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=20;text.color=Color.white;text.text=string.Empty;text.raycastTarget=false;return text;}
        /// <summary>创建独立临时按钮及中文标签。</summary>
        private static Button Button(GameObject parent,string name,string label,float x,float y){var go=Child(parent,name);Rect(go,x,y,210,52);go.AddComponent<Image>().color=new Color(.25f,.32f,.38f,1);var b=go.AddComponent<Button>();Text(go,"Label",8,8,194,36).text=label;return b;}
        /// <summary>写入必需的序列化组件引用。</summary>
        private static void Set(UnityEngine.Object target,string field,UnityEngine.Object value){var so=new SerializedObject(target);so.FindProperty(field).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
        /// <summary>写入基础根的生命周期设置。</summary>
        private static void SetBool(UnityEngine.Object target,string field,bool value){var so=new SerializedObject(target);so.FindProperty(field).boolValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    }
}
