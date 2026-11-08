#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YooAsset.Editor;
namespace Game.Editor
{
    /// <summary>只读构建依赖检查；不切平台、不生成资源、不安装工具、不执行构建或发布。</summary>
    public static class BuildPreflight
    {
        /// <summary>配置和依赖的观察结果；通过也不代表实际构建已验证。</summary>
        [Serializable] public sealed class Result
        {
            // 源文件、场景入口和收集地址必须同时存在。
            public bool BootSceneExists, MainSceneExists, GameAssemblyExists, BootIsOnlyBuildScene, MainAddressConfigured;
            // HybridCLR 包、热更程序集配置和本地 IL2CPP 是不同依赖。
            public bool HybridClrPackageExists, HybridClrGameConfigured, HybridClrLocalInstalled;
            // 各平台的构建模块和脚本后端分别检查。
            public bool WindowsModuleInstalled, AndroidModuleInstalled, WindowsIl2CppConfigured, AndroidIl2CppConfigured;
            public bool AndroidArm64Configured, AndroidLandscapeOnlyConfigured;
            // 安装器只能读取现有配置和文件，禁止预检时触发选择框或安装。
            public bool InnoCompilerExists, InstallerTemplateExists, AndroidInstallerSourcesExist;
            public bool WindowsSourceIdentityValid, HttpResourcesAllowed;
            public string ActiveTarget, CheckedUtc, InnoCompilerPath, HybridClrLocalPath;
            public string[] MissingRequirements, Limitations;
            public bool SourceReady => BootSceneExists && MainSceneExists && GameAssemblyExists && BootIsOnlyBuildScene && MainAddressConfigured && HybridClrPackageExists && HybridClrGameConfigured;
        }
        /// <summary>查询真实工程配置及工具文件，逐项保留缺失原因。</summary>
        public static Result Inspect()
        {
            var result = new Result {
                BootSceneExists = File.Exists("Assets/Game/Scenes/Boot.unity"),
                MainSceneExists = File.Exists("Assets/Game/Scenes/Main.unity"),
                GameAssemblyExists = File.Exists("Assets/Game/Game.asmdef"),
                HybridClrPackageExists = Type.GetType("HybridCLR.Editor.SettingsUtil, HybridCLR.Editor") != null,
                WindowsModuleInstalled = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64),
                AndroidModuleInstalled = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android),
                WindowsIl2CppConfigured = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) == ScriptingImplementation.IL2CPP,
                AndroidIl2CppConfigured = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP,
                AndroidArm64Configured = PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64,
                AndroidLandscapeOnlyConfigured = AndroidBuildUtility.HasLandscapeOnlyOrientation(),
                ActiveTarget = EditorUserBuildSettings.activeBuildTarget.ToString(), CheckedUtc = DateTime.UtcNow.ToString("O")
            };
            var missing = new List<string>();
            try { WindowsReleaseIdentity.ValidateSourceIdentity(); result.WindowsSourceIdentityValid=true; }
            catch (Exception error) { missing.Add("Windows 源身份校验失败："+error.Message); }
            result.HttpResourcesAllowed=PlayerSettings.insecureHttpOption==InsecureHttpOption.AlwaysAllowed;
            if(!result.HttpResourcesAllowed)missing.Add("当前通用资源服使用 HTTP，正式包传输配置与构建要求不一致。");
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            result.BootIsOnlyBuildScene = scenes.Length == 1 && scenes[0].path == "Assets/Game/Scenes/Boot.unity";
            // 只检查收集器声明，不调用 Collect/SimulateBuild 或资源构建。
            var collector = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>("Assets/AssetBundleCollectorSetting.asset");
            var package = collector?.Packages.FirstOrDefault(item => item.PackageName == "OutpostLoop");
            result.MainAddressConfigured = package != null && package.EnableAddressable && package.Groups.Any(group =>
                group.ActiveRuleName == "EnableGroup" && group.Collectors.Any(item =>
                    item.AddressRuleName == "AddressByGameRelativePathNoExtension" &&
                    (item.CollectPath == "Assets/Game/Scenes" || item.CollectPath == "Assets/Game/Scenes/Main.unity") &&
                    item.CollectorType == ECollectorType.MainAssetCollector));
            if (result.HybridClrPackageExists)
            {
                // 缺少持久配置时不读取默认值冒充配置完成。
                result.HybridClrGameConfigured = File.Exists("ProjectSettings/HybridCLRSettings.asset") &&
                    SettingsUtil.Enable && SettingsUtil.HotUpdateAssemblyNamesExcludePreserved.SequenceEqual(new[] { "Game" });
                result.HybridClrLocalPath = SettingsUtil.LocalIl2CppDir;
                result.HybridClrLocalInstalled = Directory.Exists(Path.Combine(result.HybridClrLocalPath, "libil2cpp", "hybridclr")) &&
                    File.Exists(Path.Combine(result.HybridClrLocalPath, "libil2cpp", "hybridclr", "generated", "libil2cpp-version.txt"));
                if (HybridCLRSettings.Instance.useGlobalIl2cpp)
                    missing.Add("当前配置使用全局 IL2CPP，未验证全局安装与所锁定 HybridCLR 版本兼容。");
            }
            result.InnoCompilerPath = CiToolConfiguration.Current?.InnoCompiler ?? EditorPrefs.GetString("OutpostLoop.WindowsInstaller.Compiler", "");
            if (!File.Exists(result.InnoCompilerPath))
                result.InnoCompilerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Inno Setup 6", "ISCC.exe");
            result.InnoCompilerExists = File.Exists(result.InnoCompilerPath) && string.Equals(Path.GetFileName(result.InnoCompilerPath), "ISCC.exe", StringComparison.OrdinalIgnoreCase);
            string workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            result.InstallerTemplateExists = File.Exists(Path.Combine(workspace, "Tools", "WindowsInstaller", "OutpostLoop.iss"));
            result.AndroidInstallerSourcesExist = Directory.Exists("Assets/Plugins/Android") &&
                Directory.GetFiles("Assets/Plugins/Android", "ApkInstaller.java", SearchOption.AllDirectories).Any() &&
                Directory.GetFiles("Assets/Plugins/Android", "AndroidManifest.xml", SearchOption.AllDirectories).Any();
            void Require(bool success, string reason) { if (!success) missing.Add(reason); }
            Require(result.BootSceneExists, "缺少 Boot 场景。"); Require(result.MainSceneExists, "缺少 Main 场景。");
            Require(result.GameAssemblyExists, "缺少 Game.asmdef。"); Require(result.BootIsOnlyBuildScene, "Build Settings 必须只启用 Boot 场景。");
            Require(result.MainAddressConfigured, "OutpostLoop 包缺少 Main 主资源收集器及 Scenes/Main 地址规则。");
            Require(result.HybridClrPackageExists, "缺少 HybridCLR 编辑器包。"); Require(result.HybridClrGameConfigured, "HybridCLR 未启用或热更新程序集配置不是唯一 Game。");
            Require(result.HybridClrLocalInstalled, "HybridCLR 本地 IL2CPP 安装目录或版本标记缺失。");
            Require(result.WindowsModuleInstalled, "未检测到 Windows64 构建模块。"); Require(result.AndroidModuleInstalled, "未检测到 Android 构建模块。");
            Require(result.WindowsIl2CppConfigured, "Windows 脚本后端未配置 IL2CPP。"); Require(result.AndroidIl2CppConfigured, "Android 脚本后端未配置 IL2CPP。");
            Require(result.AndroidArm64Configured, $"Android CPU 必须仅为 ARM64，当前为 {PlayerSettings.Android.targetArchitectures}。");
            Require(result.AndroidLandscapeOnlyConfigured, "Android 屏幕方向必须为自动旋转且仅允许左右横屏。");
            Require(result.InnoCompilerExists, "缺少当前安装器路径规则可找到的 ISCC.exe。"); Require(result.InstallerTemplateExists, "缺少 OutpostLoop Inno Setup 模板。");
            Require(result.AndroidInstallerSourcesExist, "缺少 Android APK 安装器 Java 或 Manifest。");
            result.MissingRequirements = missing.ToArray();
            result.Limitations = new[] { "未执行 Player、资源、HybridCLR 生成或发布。", "仅检查 Main 收集器声明，未证明资源清单与全部资源可加载。", "未验证 C++ 工具链、Android SDK/NDK/JDK、签名及真实安装；模块和文件存在不代表可打包。", "HybridCLR 本地标记存在不代表当前 Unity 版本编译验证通过。" };
            return result;
        }
        /// <summary>仅将只读检查结果写入本批次报告目录。</summary>
        [MenuItem("Tools/打包/构建前检查", false, 90)]
        public static void SaveReport()
        {
            var report = Inspect();
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backup/Artifacts/BootMainBuild-20261107"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "build-preflight.json"), JsonUtility.ToJson(report, true));
            Debug.Log("构建前检查已保存：" + directory + "；缺失项=" + report.MissingRequirements.Length + "。未验证实际构建，请查看 MissingRequirements 和 Limitations。");
        }
    }
}
#endif
