#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Game.Editor
{
    /// <summary>CI 请求只支持方案中的四种构建；默认不发布，不接受隐式工具或未知字段。</summary>
    [Serializable]
    public sealed class CiBuildRequest
    {
        public int SchemaVersion;
        public string ProjectId;
        public string RequestId;
        public string ProjectPath;
        public string Platform;
        public string BuildType;
        public string ExpectedCommit;
        public string BaselineId;
        public bool Publish;
        public string PublishBundle;
        public bool ValidateOnly;
        public bool AllowDirtySource;
        public string ResourceServerUrl;
        public string PersistentDataRoot;
        public string UnityEditor;
        public string GitExecutable;
        public string PowerShell;
        public string InnoCompiler;
        public string AndroidSdk;
        public string AndroidNdk;
        public string JavaHome;
        public string AndroidGradle;

        public BuildTarget Target() => Platform == "PC" ? BuildTarget.StandaloneWindows64 : BuildTarget.Android;

        public static CiBuildRequest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) throw new ArgumentException("CI 配置为空或超过 64 KiB。");
            try
            {
                var obj = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                var fields = typeof(CiBuildRequest).GetFields(BindingFlags.Public | BindingFlags.Instance).ToDictionary(x => x.Name);
                foreach (var property in obj.Properties())
                {
                    if (!fields.TryGetValue(property.Name, out var field)) throw new ArgumentException("未知 CI 配置字段：" + property.Name);
                    JTokenType expected = field.FieldType == typeof(bool) ? JTokenType.Boolean :
                        field.FieldType == typeof(int) ? JTokenType.Integer : JTokenType.String;
                    if (property.Value.Type != expected) throw new ArgumentException("CI 配置字段类型错误：" + property.Name);
                }
                return obj.ToObject<CiBuildRequest>() ?? throw new ArgumentException("CI 配置无效。");
            }
            catch (JsonException e) { throw new ArgumentException("CI 配置 JSON 无效。", e); }
        }

        internal static string Absolute(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value) || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                throw new ArgumentException(name + " 必须为明确的绝对路径。");
            string path = Path.GetFullPath(value);
            if (path.Length > Path.GetPathRoot(path).Length) path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            BuildDataStore.NoLinks(path);
            return path;
        }

        internal static void Tool(string value, string name, params string[] fileNames)
        {
            string path = Absolute(value, name);
            if (!File.Exists(path) || !fileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException(name + " 工具缺失或文件名不正确。");
        }

        public void Validate(string clientRoot, string reportPath)
        {
            if (SchemaVersion != 1 || ProjectId != BuildDataStore.ProjectId) throw new ArgumentException("CI 配置版本或项目不匹配。");
            BuildDataStore.Identifier(RequestId);
            if (Platform != "PC" && Platform != "Android") throw new ArgumentException("Platform 仅支持 PC 或 Android。");
            if (BuildType != "FirstPackage" && BuildType != "HostUpdate") throw new ArgumentException("BuildType 仅支持 FirstPackage 或 HostUpdate。");
            if (!Regex.IsMatch(ExpectedCommit ?? "", @"\A[0-9a-fA-F]{40}([0-9a-fA-F]{24})?\z"))
                throw new ArgumentException("ExpectedCommit 必须是固定的完整 Commit。");
            ProjectPath = Absolute(ProjectPath, nameof(ProjectPath));
            if (!string.Equals(ProjectPath, Absolute(clientRoot, nameof(clientRoot)), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("CI 项目路径与当前 Unity 工程不一致。");
            if (!File.Exists(Path.Combine(ProjectPath, "ProjectSettings", "ProjectVersion.txt"))) throw new ArgumentException("不是有效 Unity 工程。");
            if (BuildType == "HostUpdate") BuildDataStore.Identifier(BaselineId);
            else if (!string.IsNullOrEmpty(BaselineId)) throw new ArgumentException("首包不得指定宿主基线。");
            if (!Publish && !string.IsNullOrEmpty(PublishBundle)) throw new ArgumentException("关闭发布时不得指定发布目标。");
            if (Publish && BuildType == "HostUpdate" && string.IsNullOrWhiteSpace(PublishBundle))
                throw new ArgumentException("CI Host 发布必须明确指定目标发布包。");
            if (BuildType == "FirstPackage" && !string.IsNullOrEmpty(PublishBundle)) throw new ArgumentException("新首包建立独立发布包，不复用旧目标。");
            if (ValidateOnly && Publish) throw new ArgumentException("仅验证环境时不能请求发布。");
            if (!string.IsNullOrEmpty(PublishBundle)) PublishBundle = Absolute(PublishBundle, nameof(PublishBundle));
            PersistentDataRoot = Absolute(PersistentDataRoot, nameof(PersistentDataRoot));
            string checkout = Directory.GetParent(ProjectPath).FullName;
            if (BuildDataStore.IsWithin(PersistentDataRoot, checkout) || BuildDataStore.IsWithin(checkout, PersistentDataRoot) ||
                BuildDataStore.IsWithin(reportPath, PersistentDataRoot)) throw new ArgumentException("持久数据、源码及结果报告目录不得重叠。");
            if (!Uri.TryCreate(ResourceServerUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("ResourceServerUrl 必须为无凭据的 HTTP/HTTPS 地址。");
            if (Platform == "Android" && (uri.IsLoopback || uri.Host == "0.0.0.0")) throw new ArgumentException("Android 资源地址不能为回环地址。");
            Tool(UnityEditor, nameof(UnityEditor), "Unity.exe");
            Tool(GitExecutable, nameof(GitExecutable), "git.exe");
            Tool(PowerShell, nameof(PowerShell), "powershell.exe", "pwsh.exe");
            if (Platform == "PC" && BuildType == "FirstPackage") Tool(InnoCompiler, nameof(InnoCompiler), "ISCC.exe");
            if (Platform == "Android")
            {
                AndroidSdk = Absolute(AndroidSdk, nameof(AndroidSdk));
                AndroidNdk = Absolute(AndroidNdk, nameof(AndroidNdk));
                JavaHome = Absolute(JavaHome, nameof(JavaHome));
                AndroidGradle = Absolute(AndroidGradle, nameof(AndroidGradle));
                if (!File.Exists(Path.Combine(AndroidSdk, "platform-tools", "adb.exe")) ||
                    !File.Exists(Path.Combine(AndroidNdk, "ndk-build.cmd")) || !File.Exists(Path.Combine(JavaHome, "bin", "java.exe")) ||
                    !File.Exists(Path.Combine(AndroidGradle, "bin", "gradle.bat")))
                    throw new ArgumentException("显式指定的 SDK、NDK、JDK 或 Gradle 不完整。");
            }
        }
    }
}
#endif
