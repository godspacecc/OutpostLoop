#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 为资源更新公告编辑窗口提供版本绑定、语言读取、JSON 合并校验和安全保存能力。
    /// </summary>
    public static class ResourceUpdateNoticeEditorUtility
    {
        private const string PackageName = "OutpostLoop";
        private static readonly string EmptyJson =
            "{\n  \"schemaVersion\": 1,\n  \"defaultLanguage\": \"zh-CN\",\n  \"notices\": []\n}\n";

        /// <summary>
        /// 表示公告编辑器当前识别到的一种语言及其显示标签。
        /// </summary>
        public sealed class LanguageInfo
        {
            public string Code;
            public string Label;
            public bool Active;
        }

        /// <summary>
        /// 表示编辑器自动绑定出的目标版本、发布状态和可保存条件。
        /// </summary>
        public sealed class VersionBinding
        {
            // 固定编辑开始时的平台与发布版本，保存前用来识别目标变化。
            public string Platform;
            public string BundleVersion;
            public string Version;
            public string Status;
            public bool CanSave;
            public string Error;
            public string LatestBundle;
        }

        public sealed class NoticeSaveResult
        {
            public bool Success;
            public bool MaintenanceSourceSucceeded;
            public bool BundleSyncAttempted;
            public bool BundleSyncSucceeded;
            public string TargetVersion;
            public string VersionStatus;
            public string Message;
            public string Error;
        }

        /// <summary>
        /// 保存窗口中正在编辑的单版本公告内容。
        /// </summary>
        public sealed class NoticeEditState
        {
            public string Version;
            public bool Enabled = true;
            public Dictionary<string, ResourceUpdateNoticeContent> Texts =
                new Dictionary<string, ResourceUpdateNoticeContent>(StringComparer.Ordinal);
        }

        public static string WorkspaceRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        public static string NoticeFilePath =>
            Path.Combine(WorkspaceRoot, "Server", "ResourceServer", "Notices", "resource-update-notices.json");

        public static IReadOnlyList<LanguageInfo> LoadLanguages(out string defaultLanguage)
        {
            defaultLanguage = "zh-CN";
            var path = Path.Combine(Application.dataPath, "Game", "Config", "Localization", "languages.json");
            var result = new List<LanguageInfo>();
            if (!File.Exists(path))
            {
                result.Add(new LanguageInfo { Code = defaultLanguage, Label = defaultLanguage, Active = true });
                return result;
            }

            var root = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            defaultLanguage = root.Value<string>("default") ?? defaultLanguage;
            foreach (var item in root["languages"] as JArray ?? new JArray())
            {
                var code = item.Value<string>("code");
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                result.Add(new LanguageInfo
                {
                    Code = code,
                    Label = item.Value<string>("label") ?? code,
                    Active = true
                });
            }

            var resolvedDefaultLanguage = defaultLanguage;
            if (result.All(item => item.Code != resolvedDefaultLanguage))
            {
                result.Insert(0, new LanguageInfo { Code = resolvedDefaultLanguage, Label = resolvedDefaultLanguage, Active = true });
            }

            return result;
        }

        public static VersionBinding ResolveVersionBinding()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            if (target != BuildTarget.Android && target != BuildTarget.StandaloneWindows64)
            {
                return new VersionBinding { CanSave = false, Error = "请先将当前打包平台切换为 PC（Windows64）或 Android。" };
            }

            var platform = target == BuildTarget.Android ? "Android" : "PC";
            var packageRoot = Path.Combine(WorkspaceRoot, "Client", "Bundles", target.ToString(), PackageName);
            var releaseRoot = Path.Combine(WorkspaceRoot, "Release", "ResourceServer");
            if (target == BuildTarget.Android)
                releaseRoot = Path.Combine(releaseRoot, "Android");
            var templateRoot = Path.Combine(WorkspaceRoot, "Server", "ResourceServer");
            var result = ResolveVersionBinding(packageRoot, releaseRoot, templateRoot);
            result.Platform = platform;
            return result;
        }

        internal static VersionBinding ResolveVersionBinding(string packageRoot, string releaseRoot, string templateRoot)
        {
            var hostOk = YooAssetBuildUtility.TryFindLatestValidPackage(packageRoot, out _, out var hostVersion, out var hostError);
            var bundleOk = ResourceServerPublishUtility.TryFindLatestBundle(releaseRoot, out var bundle, out _);
            string bundleVersion = null;
            var compatibleBundle = bundleOk &&
                ResourceServerPublishUtility.IsNoticeCapableBundle(bundle, templateRoot, releaseRoot, out _);
            if (compatibleBundle)
            {
                var versionFile = Path.Combine(bundle, "ResourceFiles", $"{PackageName}.version");
                if (File.Exists(versionFile))
                {
                    bundleVersion = File.ReadAllText(versionFile).Trim();
                }
            }

            if (!hostOk)
            {
                return new VersionBinding { CanSave = false, Error = hostError ?? "未找到有效 Host 版本" };
            }

            if (!string.IsNullOrWhiteSpace(bundleVersion) &&
                string.CompareOrdinal(bundleVersion, hostVersion) > 0)
            {
                return new VersionBinding
                {
                    Version = hostVersion,
                    Status = "版本冲突",
                    CanSave = false,
                    Error = $"最新 ResourceServer 包版本 {bundleVersion} 高于 Host 版本 {hostVersion}。"
                };
            }

            var version = hostVersion;
            var status = "待发布";
            if (!string.IsNullOrWhiteSpace(bundleVersion) &&
                string.Equals(bundleVersion, hostVersion, StringComparison.Ordinal))
            {
                version = bundleVersion;
                status = "当前服务器包";
            }

            return new VersionBinding
            {
                Version = version,
                Status = status,
                CanSave = true,
                BundleVersion = bundleVersion,
                LatestBundle = compatibleBundle ? bundle : null
            };
        }

        // 保存时不把旧草稿重新绑定到其他平台、版本或新生成的发布目录。
        internal static bool IsSameTarget(VersionBinding edited, VersionBinding current)
        {
            return edited != null && current != null && edited.CanSave && current.CanSave &&
                string.Equals(edited.Platform, current.Platform, StringComparison.Ordinal) &&
                string.Equals(edited.Version, current.Version, StringComparison.Ordinal) &&
                string.Equals(edited.BundleVersion, current.BundleVersion, StringComparison.Ordinal) &&
                string.Equals(edited.LatestBundle, current.LatestBundle, StringComparison.OrdinalIgnoreCase);
        }

        public static NoticeEditState LoadEditState(string version, IReadOnlyList<LanguageInfo> languages)
        {
            return LoadEditState(version, languages, NoticeFilePath);
        }

        internal static NoticeEditState LoadEditState(string version, IReadOnlyList<LanguageInfo> languages, string noticeFilePath)
        {
            // 打开窗口只读取；没有公告时在内存初始化，首次有效保存才创建文件。
            var json = File.Exists(noticeFilePath) ? File.ReadAllText(noticeFilePath, Encoding.UTF8) : EmptyJson;
            var parsed = ResourceUpdateNoticeDocument.TryParse(json, out _);
            if (!parsed.Success)
                throw new InvalidOperationException($"公告源文件无效，未重新加载：{parsed.Error}");
            var root = JObject.Parse(json);
            var record = (root["notices"] as JArray)?
                .OfType<JObject>()
                .FirstOrDefault(item => string.Equals(item.Value<string>("version"), version, StringComparison.Ordinal));
            var state = new NoticeEditState { Version = version, Enabled = record?.Value<bool?>("enabled") ?? true };
            var texts = record?["texts"] as JObject;
            foreach (var language in languages)
            {
                var text = texts?[language.Code] as JObject;
                var items = (text?["items"] as JArray)?
                    .Values<string>()
                    .Where(item => item != null)
                    .ToArray() ?? Array.Empty<string>();
                state.Texts[language.Code] = new ResourceUpdateNoticeContent(
                    version,
                    text?.Value<string>("title") ?? string.Empty,
                    text?.Value<string>("summary") ?? string.Empty,
                    items);
            }

            if (texts != null)
            {
                foreach (var property in texts.Properties())
                {
                    if (state.Texts.ContainsKey(property.Name) || property.Value.Type != JTokenType.Object)
                    {
                        continue;
                    }

                    var text = (JObject)property.Value;
                    state.Texts[property.Name] = new ResourceUpdateNoticeContent(
                        version,
                        text.Value<string>("title") ?? string.Empty,
                        text.Value<string>("summary") ?? string.Empty,
                        (text["items"] as JArray)?.Values<string>().ToArray() ?? Array.Empty<string>());
                }
            }

            return state;
        }

        public static string Save(NoticeEditState state, string defaultLanguage, string latestBundle)
        {
            return Save(state, defaultLanguage, latestBundle, string.Empty);
        }

        public static string Save(
            NoticeEditState state,
            string defaultLanguage,
            string latestBundle,
            string versionStatus)
        {
            return SaveDetailed(state, defaultLanguage, latestBundle, null, versionStatus).Message;
        }

        internal static NoticeSaveResult SaveDetailed(
            NoticeEditState state,
            string defaultLanguage,
            string latestBundle,
            string noticeFilePath = null,
            string versionStatus = "")
        {
            noticeFilePath = string.IsNullOrWhiteSpace(noticeFilePath) ? NoticeFilePath : noticeFilePath;
            var originalJson = File.Exists(noticeFilePath) ? File.ReadAllText(noticeFilePath, Encoding.UTF8) : EmptyJson;
            var originalParse = ResourceUpdateNoticeDocument.TryParse(originalJson, out _);
            if (!originalParse.Success)
                throw new InvalidOperationException($"公告源文件无效，未覆盖：{originalParse.Error}");
            var root = JObject.Parse(originalJson);
            root["schemaVersion"] = 1;
            root["defaultLanguage"] = defaultLanguage;
            var notices = root["notices"] as JArray ?? new JArray();
            var old = notices.OfType<JObject>()
                .FirstOrDefault(item => string.Equals(item.Value<string>("version"), state.Version, StringComparison.Ordinal));
            old?.Remove();
            notices.Add(ToJsonRecord(state));
            root["notices"] = new JArray(notices
                .OfType<JObject>()
                .OrderByDescending(item => item.Value<string>("version"), StringComparer.Ordinal));

            var json = root.ToString(Formatting.Indented) + "\n";
            var parse = ResourceUpdateNoticeDocument.TryParse(json, out _);
            if (!parse.Success)
            {
                return new NoticeSaveResult
                {
                    Success = false,
                    MaintenanceSourceSucceeded = false,
                    TargetVersion = state.Version,
                    VersionStatus = versionStatus ?? string.Empty,
                    Message = $"公告校验失败，源文件未保存：{parse.Error}",
                    Error = parse.Error
                };
            }

            try
            {
                SafeReplace(noticeFilePath, json);
            }
            catch (Exception exception)
            {
                return new NoticeSaveResult
                {
                    Success = false,
                    MaintenanceSourceSucceeded = false,
                    TargetVersion = state.Version,
                    VersionStatus = versionStatus ?? string.Empty,
                    Message = $"公告源文件保存失败：{noticeFilePath}\n{exception.Message}",
                    Error = exception.Message
                };
            }

            var message = BuildSaveMessage("公告源文件已保存", state.Version, versionStatus) + $"\n源文件：{noticeFilePath}";
            if (!string.IsNullOrWhiteSpace(latestBundle))
            {
                try
                {
                    if (!Directory.Exists(latestBundle))
                        throw new DirectoryNotFoundException($"目标发布目录已不存在：{latestBundle}");
                    var target = Path.Combine(latestBundle, "Notices", "resource-update-notices.json");
                    SafeReplace(target, json);
                    return new NoticeSaveResult
                    {
                        Success = true,
                        MaintenanceSourceSucceeded = true,
                        BundleSyncAttempted = true,
                        BundleSyncSucceeded = true,
                        TargetVersion = state.Version,
                        VersionStatus = versionStatus ?? string.Empty,
                        Message = message + $"\n发布目录公告已同步：{target}\n若资源服正在使用该目录，后续公告请求可读到新内容；未上传到其他设备。"
                    };
                }
                catch (Exception exception)
                {
                    return new NoticeSaveResult
                    {
                        Success = false,
                        MaintenanceSourceSucceeded = true,
                        BundleSyncAttempted = true,
                        BundleSyncSucceeded = false,
                        TargetVersion = state.Version,
                        VersionStatus = versionStatus ?? string.Empty,
                        Message = message + $"\n发布目录同步失败，源文件未回退：{latestBundle}\n{exception.Message}\n编辑内容已保留，修复后可重试。",
                        Error = exception.Message
                    };
                }
            }

            return new NoticeSaveResult
            {
                Success = true,
                MaintenanceSourceSucceeded = true,
                BundleSyncAttempted = false,
                BundleSyncSucceeded = false,
                TargetVersion = state.Version,
                VersionStatus = versionStatus ?? string.Empty,
                Message = message + "\n没有兼容的发布目录，本次仅保存源文件；后续生成或更新资源服时带入。"
            };
        }

        private static string BuildSaveMessage(string prefix, string version, string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return $"{prefix}；目标版本：{version}";
            }

            return $"{prefix}；目标版本：{version}；状态：{status}";
        }
        private static JObject ToJsonRecord(NoticeEditState state)
        {
            var texts = new JObject();
            foreach (var pair in state.Texts.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var content = pair.Value;
                texts[pair.Key] = new JObject
                {
                    ["title"] = content.Title ?? string.Empty,
                    ["summary"] = content.Summary ?? string.Empty,
                    ["items"] = new JArray((content.Items ?? Array.Empty<string>()).Where(item => item != null))
                };
            }

            return new JObject
            {
                ["version"] = state.Version,
                ["enabled"] = state.Enabled,
                ["texts"] = texts
            };
        }

        private static void SafeReplace(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            var parse = ResourceUpdateNoticeDocument.TryParse(File.ReadAllText(temp, Encoding.UTF8), out _);
            if (!parse.Success)
            {
                File.Delete(temp);
                throw new InvalidOperationException($"公告保存失败：{parse.Error}");
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }

                throw;
            }
        }
    }
}
#endif
