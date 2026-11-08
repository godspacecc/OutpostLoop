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
    /// <summary>创建独立启动界面并拆分场景；不构建、不发布、不导入旧视觉。</summary>
    public static class BootMainSceneSetup
    {
        /// <summary>宿主场景路径，唯一随 Player 打包的入口。</summary>
        public const string BootPath="Assets/Game/Scenes/Boot.unity";
        /// <summary>主场景路径，以 Scenes/Main 地址加入资源包。</summary>
        public const string MainPath="Assets/Game/Scenes/Main.unity";
        [MenuItem("Tools/开发/初始化Boot与Main")]
        /// <summary>生成启动场景并配置独立主场景；不会执行构建。</summary>
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
            var prototype = UnityEngine.Object.FindObjectOfType<PrototypeController>();
            Set(prototype,"previewShader",Shader.Find("Standard"));
            Set(prototype,"startArtwork",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Start/OutpostConcept.png"));
            EditorSceneManager.SaveScene(main);
            var boot=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EnsureBootEnvironment(boot);
            var canvasObject=new GameObject("BootCanvas",typeof(RectTransform));
            canvasObject.SetActive(false);
            var canvas=canvasObject.AddComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder=1000;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
            canvasObject.AddComponent<TemporaryBootFont>();
            var entry=new GameObject("BootEntry");
            entry.SetActive(false);
            var launcher=entry.AddComponent<Launcher>();
            BuildBootVisual(canvas, launcher);
            // Boot 输入根跟随 Boot 场景卸载，避免进入 Main 后出现两个事件系统。
            new GameObject("BootEventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
            canvasObject.SetActive(true);entry.SetActive(true);
            // Canvas 首次激活会重置原生排序标记，激活后再持久化覆盖设置。
            SetBool(canvas,"m_OverrideSorting",true);
            EditorSceneManager.SaveScene(boot,BootPath);EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(BootPath,true)};
            PlayerSettings.companyName="Godspace";PlayerSettings.productName="OutpostLoop";
            // 参考构建链使用本机 HTTP 资源服，正式 Windows 包同样需要允许该传输。
            PlayerSettings.insecureHttpOption=InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,"com.godspace.outpostloop");PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone,"com.godspace.outpostloop");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.IL2CPP);PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,ScriptingImplementation.IL2CPP);
            // 与完整 Android APK 的目标平台和横屏约束保持一致。
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            var hybrid=HybridCLR.Editor.Settings.HybridCLRSettings.Instance;hybrid.hotUpdateAssemblyDefinitions=new[]{AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>("Assets/Game/Game.asmdef")};HybridCLR.Editor.Settings.HybridCLRSettings.Save();
            var host="Assets/StreamingAssets/hostConfig.json";if(!File.Exists(host))File.WriteAllText(host,"{\"resourceServer\":\"http://127.0.0.1:8000\"}");
            File.WriteAllText("Assets/Game/Dlls/.gitkeep",string.Empty);
            EnsureCollectors();AssetDatabase.SaveAssets();AssetDatabase.Refresh();
            Debug.Log("Boot/Main 已拆分并生成启动界面。");
        }

        /// <summary>在现有 Boot 场景内重新生成界面，沿用已保存的 Launcher、Canvas 和输入组件。</summary>
        public static void RefreshBootVisual()
        {
            if (EditorApplication.isPlaying || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new InvalidOperationException("请退出 Play 并保存场景后再生成 Boot 界面。");

            var scene = EditorSceneManager.OpenScene(BootPath);
            EnsureBootEnvironment(scene);
            var launcher = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Launcher>(true)).Single();
            var settings = new SerializedObject(launcher);
            var canvas = (Canvas)settings.FindProperty("_updateCanvas").objectReferenceValue;
            var oldView = (HotUpdateView)settings.FindProperty("_updateView").objectReferenceValue;
            BuildBootVisual(canvas, launcher);
            if (oldView) UnityEngine.Object.DestroyImmediate(oldView.gameObject);
            canvas.name = "BootCanvas";
            SetBool(canvas, "m_OverrideSorting", true);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>补齐参考 Boot 的场景级组件，随 Boot 场景卸载。</summary>
        private static void EnsureBootEnvironment(UnityEngine.SceneManagement.Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            if (!roots.Any(root => root.CompareTag("MainCamera") && root.TryGetComponent<Camera>(out _)))
            {
                var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(0, 1, -10);
                camera.GetComponent<Camera>().depth = -1;
            }
            if (!roots.Any(root => root.TryGetComponent<Light>(out var light) && light.type == LightType.Directional))
            {
                var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
                light.type = LightType.Directional;
                light.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(50, -30, 0));
                light.color = new Color32(255, 244, 214, 255);
                light.shadows = LightShadows.Soft;
            }
        }

        private static readonly Color BootInk = new Color32(237, 244, 239, 255);
        private static readonly Color BootMuted = new Color32(158, 179, 170, 255);
        private static readonly Color BootLine = new Color32(54, 77, 66, 255);
        private static readonly Color BootMint = new Color32(139, 214, 187, 255);
        private static readonly Color BootAmber = new Color32(241, 188, 130, 255);

        /// <summary>生成 1280×720 的信标布局，显示与操作继续使用原启动视图的字段。</summary>
        private static void BuildBootVisual(Canvas canvas, Launcher launcher)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var panel = Child(canvas.gameObject, "BootSignalPanel");
            panel.AddComponent<Image>().color = new Color32(15, 25, 21, 255);
            var view = panel.AddComponent<HotUpdateView>();
            var layout = Centered(panel, "Layout", 1280, 720);
            Text(layout, "Wordmark", 64, 31, 400, 32, 14, BootInk, TextAnchor.MiddleLeft, "◇  OUTPOST LOOP");
            Text(layout, "BootCaption", 1060, 31, 156, 32, 14, BootMuted, TextAnchor.MiddleRight, "BOOT");
            Box(layout, "FooterRule", 64, 662, 1152, 1, BootLine);
            Set(view, "_versionText", Text(layout, "Version", 64, 668, 1152, 32, 14, BootMuted,
                TextAnchor.MiddleLeft, "资源版本：检测中"));

            var signal = Child(layout, "Beacon");
            Rect(signal, 548, 173, 184, 184);
            var beacon = signal.AddComponent<BootBeaconGraphic>();
            beacon.color = BootMint;
            beacon.raycastTarget = false;
            Set(view, "_beacon", beacon);
            Set(view, "_stageText", Text(layout, "Stage", 300, 372, 680, 32, 15, BootMint));
            Set(view, "_statusText", Text(layout, "Status", 250, 401, 780, 55, 30, BootInk, value: "正在准备启动...", bold: true));
            Set(view, "_titleText", Hidden(Text(layout, "Title", 250, 401, 780, 55, 30, BootInk, bold: true)));
            Set(view, "_guidanceText", Hidden(Text(layout, "Guidance", 320, 455, 640, 52, 16, BootMuted)));
            Set(view, "_detailsText", Hidden(Text(layout, "Details", 320, 588, 640, 68, 12, BootMuted, TextAnchor.UpperCenter)));
            Set(view, "_copyNotice", Text(layout, "CopyNotice", 320, 565, 640, 22, 13, BootMint));

            var busy = Box(layout, "BusyIndicator", 604, 502, 72, 2, BootLine);
            Box(busy, "Signal", 24, 0, 24, 2, BootMint);
            Set(view, "_busyIndicator", busy);

            var progress = Child(layout, "Progress");
            var track = Box(progress, "Slider", 405, 478, 470, 4, BootLine);
            var slider = track.AddComponent<Slider>();
            slider.interactable = false;
            var fill = Child(track, "Fill");
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = BootMint;
            fillImage.raycastTarget = false;
            slider.fillRect = (RectTransform)fill.transform;
            Set(view, "_progressArea", progress);
            Set(view, "_progressSlider", slider);
            Set(view, "_progressText", Text(progress, "Percent", 797, 489, 78, 30, 15, BootMint, TextAnchor.MiddleRight, "0%"));
            progress.SetActive(false);

            var stats = Child(layout, "DownloadStats");
            Set(view, "_downloadStatsArea", stats);
            Set(view, "_sizeText", Text(stats, "Size", 405, 489, 218, 30, 14, BootMuted, TextAnchor.MiddleLeft, "大小: -- / --"));
            Set(view, "_speedText", Text(stats, "Speed", 623, 489, 174, 30, 14, BootMuted, value: "速度: --"));
            Set(view, "_fileCountText", Hidden(Text(stats, "Files", 405, 518, 470, 24, 14, BootMuted, value: "文件: -- / --")));
            stats.SetActive(false);

            var buttons = Child(layout, "Buttons");
            Set(view, "_buttonArea", buttons);
            Set(view, "_retryButton", Hidden(Button(buttons, "Retry", "重新尝试", 424, 526, 100, true)));
            Set(view, "_copyDetailsButton", Hidden(Button(buttons, "Copy", "复制错误", 636, 526, 126)));
            Set(view, "_quitButton", Hidden(Button(buttons, "Quit", "退出游戏", 774, 526, 82)));
            Set(launcher, "_updateView", view);
            Set(launcher, "_updateCanvas", canvas);

            var server = panel.AddComponent<BootResourceServerView>();
            Set(server, "_launcher", launcher);
            Set(server, "_entryButton", Hidden(Button(buttons, "Server", "重设服务器", 536, 526, 88)));
            buttons.SetActive(false);
            BuildServerEditor(panel, server);
        }

        /// <summary>地址编辑采用独立遮罩，保留当前地址、校验反馈和三个原有动作。</summary>
        private static void BuildServerEditor(GameObject panel, BootResourceServerView server)
        {
            var overlay = Child(panel, "ServerEditor");
            overlay.AddComponent<Image>().color = new Color32(8, 14, 12, 230);
            Set(server, "_editorPanel", overlay);
            var card = Centered(overlay, "AddressCard", 640, 304);
            card.AddComponent<Image>().color = new Color32(24, 39, 33, 255);
            Text(card, "EditorTitle", 36, 26, 568, 40, 26, BootInk, TextAnchor.MiddleLeft, "资源服务器地址", true);
            Set(server, "_currentAddressText", Text(card, "Current", 36, 76, 568, 38, 15, BootMuted,
                TextAnchor.MiddleLeft, "当前地址：读取中"));

            var field = Box(card, "Address", 36, 120, 568, 44, BootLine);
            var input = field.AddComponent<InputField>();
            input.contentType = InputField.ContentType.Custom;
            input.keyboardType = TouchScreenKeyboardType.URL;
            input.characterLimit = 1024;
            input.targetGraphic = field.GetComponent<Image>();
            input.targetGraphic.raycastTarget = true;
            input.textComponent = Text(field, "Value", 12, 0, 544, 44, 17, BootInk, TextAnchor.MiddleLeft);
            input.placeholder = Text(field, "Placeholder", 12, 0, 544, 44, 17, BootMuted,
                TextAnchor.MiddleLeft, "http://服务器地址:端口");
            Set(server, "_addressInput", input);
            Set(server, "_feedbackText", Text(card, "Feedback", 36, 176, 568, 56, 15, BootMuted,
                TextAnchor.UpperLeft, "请输入资源服务器的完整 HTTP/HTTPS 地址和端口。"));
            Set(server, "_saveButton", Button(card, "Save", "保存并重试", 36, 238, 148, true));
            Set(server, "_resetButton", Button(card, "Reset", "恢复默认并重试", 196, 238, 122));
            Set(server, "_cancelButton", Button(card, "Cancel", "取消", 330, 238, 90));
            overlay.SetActive(false);
        }

        /// <summary>让序列化场景与进入启动状态后的控件显隐一致。</summary>
        private static T Hidden<T>(T component) where T : Component
        {
            component.gameObject.SetActive(false);
            return component;
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
        private static GameObject Centered(GameObject parent, string name, float width, float height)
        {
            var go = Child(parent, name);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
            return go;
        }

        private static GameObject Box(GameObject parent, string name, float x, float y, float width, float height, Color color)
        {
            var go = Child(parent, name);
            Rect(go, x, y, width, height);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return go;
        }

        /// <summary>创建启动文本，动态文案交由视图填写。</summary>
        private static Text Text(GameObject parent, string name, float x, float y, float width, float height, int size,
            Color color, TextAnchor alignment = TextAnchor.MiddleCenter, string value = "", bool bold = false)
        {
            var go = Child(parent, name);
            Rect(go, x, y, width, height);
            var text = go.AddComponent<Text>();
            text.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.color = color;
            text.text = value;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>绘制按钮并为指针输入显式绑定可命中的图像。</summary>
        private static Button Button(GameObject parent, string name, string label, float x, float y, float width, bool primary = false)
        {
            var go = Box(parent, name, x, y, width, 38, primary ? BootAmber : BootLine);
            var button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            button.targetGraphic.raycastTarget = true;
            Text(go, "Label", 0, 0, width, 38, 14, primary ? new Color32(39, 29, 20, 255) : BootInk, value: label, bold: primary);
            return button;
        }
        /// <summary>写入必需的序列化组件引用。</summary>
        private static void Set(UnityEngine.Object target,string field,UnityEngine.Object value){var so=new SerializedObject(target);so.FindProperty(field).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
        /// <summary>写入基础根的生命周期设置。</summary>
        private static void SetBool(UnityEngine.Object target,string field,bool value){var so=new SerializedObject(target);so.FindProperty(field).boolValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    }
}
