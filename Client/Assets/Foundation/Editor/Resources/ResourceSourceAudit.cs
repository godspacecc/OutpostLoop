using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;
namespace OutpostLoop.Resources.Editor
{
    /// <summary>独立文件名地址，保留 bytes 后缀。</summary>
    public sealed class OutpostConfigAddress : IAddressRule
    { public string GetAssetAddress(AddressRuleData data) => Path.GetFileName(data.AssetPath); }
    /// <summary>仅收集配置目录直属 bytes，避免顺带收集视觉资源。</summary>
    public sealed class OutpostConfigFilter : IFilterRule
    {
        public string FindAssetType => EAssetSearchType.All.ToString();
        public bool IsCollectAsset(FilterRuleData data) => Path.GetDirectoryName(data.AssetPath).Replace('\\', '/') == "Assets/Game/Config" && Path.GetExtension(data.AssetPath) == ".bytes";
    }
    /// <summary>UI 使用 Game 根下不含扩展名的统一逻辑地址。</summary>
    public sealed class OutpostUiAddress : IAddressRule
    { public string GetAssetAddress(AddressRuleData data) => data.AssetPath.Substring("Assets/Game/".Length, data.AssetPath.Length-"Assets/Game/".Length-".prefab".Length); }
    /// <summary>仅收集 UI Prefab，不包含源码或测试报告。</summary>
    public sealed class OutpostUiFilter : IFilterRule
    {
        public string FindAssetType => EAssetSearchType.All.ToString();
        public bool IsCollectAsset(FilterRuleData data) => data.AssetPath.StartsWith("Assets/Game/Prefabs/UI/",StringComparison.Ordinal) && data.AssetPath.EndsWith(".prefab",StringComparison.Ordinal);
    }
    public static class ResourceSourceAudit
    {
        public const string SettingPath = "Assets/AssetBundleCollectorSetting.asset";
        /// <summary>只创建本项目设置；遇到已有外来包拒绝覆盖。</summary>
        [MenuItem("OutpostLoop/Resources/Ensure Settings")]
        public static void EnsureSettings()
        {
            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(SettingPath);
            if (setting != null) {
                if (setting.Packages.Count != 1 || setting.Packages[0].PackageName != ResourcePackageRuntime.PackageName)
                    throw new InvalidOperationException("检测到非本项目资源设置，拒绝覆盖。");
                EnsureUiGroup(setting);
                return;
            }
            setting = ScriptableObject.CreateInstance<AssetBundleCollectorSetting>();
            var package = new AssetBundleCollectorPackage { PackageName = ResourcePackageRuntime.PackageName, EnableAddressable = true, SupportExtensionless = false, AutoCollectShaders = false };
            var group = new AssetBundleCollectorGroup { GroupName = "Config", ActiveRuleName = "EnableGroup", AssetTags = "startup" };
            group.Collectors.Add(new AssetBundleCollector { CollectPath = "Assets/Game/Config", CollectorGUID = AssetDatabase.AssetPathToGUID("Assets/Game/Config"), CollectorType = ECollectorType.MainAssetCollector, AddressRuleName = nameof(OutpostConfigAddress), PackRuleName = "PackSeparately", FilterRuleName = nameof(OutpostConfigFilter) });
            package.Groups.Add(group); setting.Packages.Add(package);
            AssetDatabase.CreateAsset(setting, SettingPath); EnsureUiGroup(setting); AssetDatabase.SaveAssets();
        }
        /// <summary>补齐空 UI 目录的收集规则，不创建任何正式视觉资源。</summary>
        private static void EnsureUiGroup(AssetBundleCollectorSetting setting)
        {
            var package=setting.Packages[0];
            if(package.Groups.Any(g=>g.GroupName=="UI"))return;
            Directory.CreateDirectory("Assets/Game/Prefabs/UI"); AssetDatabase.Refresh();
            var group=new AssetBundleCollectorGroup{GroupName="UI",ActiveRuleName="EnableGroup",AssetTags="startup"};
            group.Collectors.Add(new AssetBundleCollector{CollectPath="Assets/Game/Prefabs/UI",CollectorGUID=AssetDatabase.AssetPathToGUID("Assets/Game/Prefabs/UI"),CollectorType=ECollectorType.MainAssetCollector,AddressRuleName=nameof(OutpostUiAddress),PackRuleName="PackSeparately",FilterRuleName=nameof(OutpostUiFilter)});
            package.Groups.Add(group); EditorUtility.SetDirty(setting); AssetDatabase.SaveAssets();
        }
        /// <summary>调用正式收集器，仅查询源结果，不执行任何构建。</summary>
        [MenuItem("OutpostLoop/Resources/Audit Source Collection")]
        public static void Audit()
        {
            EnsureSettings();
            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(SettingPath);
            var result = setting.BeginCollect(ResourcePackageRuntime.PackageName, true, false);
            var config = result.CollectAssets.Single(x => x.Address == "tbrule.bytes");
            if (config.AssetInfo.AssetPath != "Assets/Game/Config/tbrule.bytes" || !config.AssetTags.Contains("startup")) throw new InvalidOperationException("配置地址或启动标签不匹配。");
            if (result.CollectAssets.GroupBy(x => x.Address).Any(g => g.Count() > 1)) throw new InvalidOperationException("重复地址。");
            Debug.Log("源收集通过：tbrule.bytes / startup；运行包、发布包尚未验证。");
        }
    }
}
