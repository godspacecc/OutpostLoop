using System;
using System.IO;
using System.Linq;
using System.Text.Json;

// 仅替代引擎环境和 YooAsset 校验边界；发布事务、清单和哈希使用产品源码。
namespace UnityEngine
{
    public static class Application
    {
        public static string dataPath;
        public static string streamingAssetsPath => Path.Combine(dataPath, "StreamingAssets");
        public static bool isBatchMode => true;
    }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json)
        {
            try { return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { IncludeFields = true }); }
            catch (JsonException e) { throw new ArgumentException("Invalid JSON", e); }
        }
        public static string ToJson(object value, bool pretty) => JsonSerializer.Serialize(value,
            new JsonSerializerOptions { IncludeFields = true, WriteIndented = pretty });
    }
}
namespace UnityEditor
{
    public enum BuildTarget { StandaloneWindows64, Android }
    public static class EditorApplication
    {
        public static bool isCompiling, isUpdating, isPlayingOrWillChangePlaymode;
    }
    public static class EditorUserBuildSettings { public static BuildTarget activeBuildTarget; }
    public static class EditorPrefs
    {
        public static string GetString(string key, string fallback) => fallback;
        public static void SetString(string key, string value) { }
    }
    public static class EditorUtility
    {
        public static string OpenFilePanel(string title, string directory, string extension) =>
            throw new InvalidOperationException("检查禁止打开交互文件选择器。");
    }
    public enum ImportAssetOptions { ForceSynchronousImport }
    public static class AssetDatabase
    {
        public static void ImportAsset(string path, ImportAssetOptions options) { }
        public static bool DeleteAsset(string path) { if (File.Exists(path)) File.Delete(path); return true; }
    }
}
namespace Cysharp.Threading.Tasks { public struct UniTask<T> { } }
namespace QFramework { public interface IUtility { } }
namespace Game { public struct ResourceUpdateSnapshot { } }
namespace Game.Editor
{
    public static class BuildUtility { public static bool HasActiveWorkflow; }
    public static class AndroidBuildUtility { public static bool HasActiveWorkflow; }
    public static class YooAssetBuildUtility
    {
        public static string GetPackageRoot(UnityEditor.BuildTarget target) => Path.GetFullPath(Path.Combine(
            UnityEngine.Application.dataPath, "..", "Bundles", target.ToString(), "OutpostLoop"));
        public static bool ValidateBuildResult(string directory, string version) =>
            File.Exists(Path.Combine(directory, "OutpostLoop.version")) &&
            File.ReadAllText(Path.Combine(directory, "OutpostLoop.version")).Trim() == version &&
            File.Exists(Path.Combine(directory, "payload.bundle"));
        public static bool TryFindLatestValidPackage(string root, out string source, out string version, out string error)
        {
            source = Directory.GetDirectories(root).OrderByDescending(x => x).FirstOrDefault(x =>
                ValidateBuildResult(x, Path.GetFileName(x)));
            version = source == null ? null : Path.GetFileName(source);
            error = source == null ? "没有有效资源" : null;
            return source != null;
        }
    }
}
