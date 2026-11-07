#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Boot;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>为完整包配对内置宿主与资源身份；热更新只能恢复不可变基线的身份，不能临时创建。</summary>
    public static class HostCompatibilityBuild
    {
        public static void WriteNew(BuildTarget target)
        {
            Write(target, CreateDescriptor(target));
        }

        public static string CreateDescriptor(BuildTarget target)
        {
            var descriptor = new HostCompatibilityDescriptor
            { schemaVersion = 1, hostId = Guid.NewGuid().ToString("N"), platform = target.ToString() };
            string json = JsonUtility.ToJson(descriptor);
            if (!HostCompatibilityDescriptor.Matches(json, json, target.ToString(), out var reason))
                throw new InvalidOperationException("不支持的完整包平台：" + reason);
            return json;
        }

        /// <summary>在清理生成目录之前读取并验证旧基线，失败时不改变已有生成物。</summary>
        public static string ReadBaselineDescriptor(BuildTarget target, string baselineDllDirectory)
        {
            string path = Path.Combine(baselineDllDirectory, HostCompatibilityDescriptor.AssetAddress);
            if (!File.Exists(path))
                throw new InvalidOperationException("旧基线没有运行时宿主身份，必须先构建新的完整包。");
            string json = File.ReadAllText(path);
            if (!HostCompatibilityDescriptor.Matches(json, json, target.ToString(), out var reason))
                throw new InvalidOperationException("基线宿主身份无效：" + reason);
            return json;
        }

        /// <summary>资源打包前拒绝缺失、跨平台或与当前内置身份不一致的输入。</summary>
        public static void ValidatePrepared(BuildTarget target)
        {
            string embedded = Path.Combine(Application.streamingAssetsPath, HostCompatibilityDescriptor.EmbeddedFileName);
            string resource = Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), HostCompatibilityDescriptor.AssetAddress);
            if (!File.Exists(embedded) || !File.Exists(resource))
                throw new InvalidOperationException("缺少配对宿主身份，请先生成完整包输入或恢复新格式基线。");
            if (!HostCompatibilityDescriptor.Matches(File.ReadAllText(embedded), File.ReadAllText(resource), target.ToString(), out var reason))
                throw new InvalidOperationException("资源打包宿主身份不匹配：" + reason);
        }

        public static void Write(BuildTarget target, string json)
        {
            if (!HostCompatibilityDescriptor.Matches(json, json, target.ToString(), out string reason))
                throw new InvalidOperationException("构建宿主身份无效：" + reason);
            File.WriteAllText(Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), HostCompatibilityDescriptor.AssetAddress),
                json, new UTF8Encoding(false));
            Directory.CreateDirectory(Application.streamingAssetsPath);
            File.WriteAllText(Path.Combine(Application.streamingAssetsPath, HostCompatibilityDescriptor.EmbeddedFileName),
                json, new UTF8Encoding(false));
        }
    }
}
#endif
