using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>
    /// 解析、校验、选择和格式化资源更新公告 JSON，供编辑器、服务端校验和运行时共用规则。
    /// </summary>
    public sealed class ResourceUpdateNoticeDocument
    {
        public const int MaxBytes = 256 * 1024;
        private const int MaxNotices = 100;
        private const int MaxTitleCodePoints = 100;
        private const int MaxSummaryCodePoints = 500;
        private const int MaxItems = 20;
        private const int MaxItemCodePoints = 200;
        private static readonly Regex VersionRegex =
            new Regex(@"^\d{12}$", RegexOptions.Compiled);
        private static readonly Regex RichTextRegex =
            new Regex(@"<[^>\r\n]+>", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private ResourceUpdateNoticeDocument(
            string defaultLanguage,
            IReadOnlyList<NoticeRecord> notices)
        {
            DefaultLanguage = defaultLanguage;
            Notices = notices;
        }

        public string DefaultLanguage { get; }
        public IReadOnlyList<NoticeRecord> Notices { get; }

        public static ResourceUpdateNoticeResult TryParse(string json, out ResourceUpdateNoticeDocument document)
        {
            document = null;
            if (json == null)
            {
                return ResourceUpdateNoticeResult.Fail("公告解析失败：JSON 为空");
            }

            var bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes > MaxBytes)
            {
                return ResourceUpdateNoticeResult.Fail("公告解析失败：文件超过 256 KiB");
            }

            try
            {
                if (json.Length > 0 && json[0] == '\uFEFF')
                {
                    return ResourceUpdateNoticeResult.Fail("公告解析失败：不允许 BOM");
                }

                var root = JObject.Parse(json);
                if (root.Value<int?>("schemaVersion") != 1)
                {
                    return ResourceUpdateNoticeResult.Fail("公告结构错误：schemaVersion 必须为 1");
                }

                var defaultLanguage = ReadRequiredString(root, "defaultLanguage", "schema");
                ValidatePlainText(defaultLanguage, 64, "schema.defaultLanguage");

                var noticesToken = root["notices"];
                if (noticesToken == null || noticesToken.Type != JTokenType.Array)
                {
                    return ResourceUpdateNoticeResult.Fail("公告结构错误：notices 必须是数组");
                }

                var noticesArray = (JArray)noticesToken;
                if (noticesArray.Count > MaxNotices)
                {
                    return ResourceUpdateNoticeResult.Fail("公告结构错误：notices count 超过 100");
                }

                var versions = new HashSet<string>(StringComparer.Ordinal);
                var records = new List<NoticeRecord>();
                for (var i = 0; i < noticesArray.Count; i++)
                {
                    if (noticesArray[i].Type != JTokenType.Object)
                    {
                        return ResourceUpdateNoticeResult.Fail($"公告记录错误：notice[{i}] 必须是对象");
                    }

                    var record = ParseNotice((JObject)noticesArray[i], i, defaultLanguage);
                    if (!versions.Add(record.Version))
                    {
                        return ResourceUpdateNoticeResult.Fail($"公告记录错误：notice[{i}] duplicate version {record.Version}");
                    }

                    records.Add(record);
                }

                document = new ResourceUpdateNoticeDocument(
                    defaultLanguage,
                    records.OrderByDescending(item => item.Version, StringComparer.Ordinal).ToArray());
                return ResourceUpdateNoticeResult.NoNotice;
            }
            catch (Exception exception) when (
                exception is Newtonsoft.Json.JsonException ||
                exception is InvalidOperationException ||
                exception is ArgumentException)
            {
                return ResourceUpdateNoticeResult.Fail(exception.Message);
            }
        }

        public static ResourceUpdateNoticeResult Select(
            ResourceUpdateNoticeDocument document,
            string version,
            string languageCode)
        {
            if (document == null)
            {
                return ResourceUpdateNoticeResult.Fail("公告选择失败：document 为空");
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                return ResourceUpdateNoticeResult.NoNotice;
            }

            var record = document.Notices.FirstOrDefault(item =>
                item.Enabled && string.Equals(item.Version, version, StringComparison.Ordinal));
            if (record == null)
            {
                return ResourceUpdateNoticeResult.NoNotice;
            }

            if (record.TryGetDisplayable(languageCode, out var current))
            {
                return ResourceUpdateNoticeResult.Ok(current);
            }

            return record.TryGetDisplayable(document.DefaultLanguage, out var fallback)
                ? ResourceUpdateNoticeResult.Ok(fallback)
                : ResourceUpdateNoticeResult.NoNotice;
        }

        public static string FormatForNotify(ResourceUpdateNoticeContent content, bool includeTitle)
        {
            var lines = new List<string>();
            if (includeTitle && !string.IsNullOrWhiteSpace(content.Title))
            {
                lines.Add(content.Title.Trim());
            }

            if (!string.IsNullOrWhiteSpace(content.Summary))
            {
                lines.Add(content.Summary.Trim());
            }

            var number = 1;
            for (var i = 0; i < content.Items.Count; i++)
            {
                var item = content.Items[i];
                if (!string.IsNullOrWhiteSpace(item))
                {
                    lines.Add($"{number}. {item.Trim()}");
                    number++;
                }
            }

            return string.Join("\n", lines);
        }

        private static NoticeRecord ParseNotice(JObject obj, int index, string defaultLanguage)
        {
            var version = ReadRequiredString(obj, "version", $"notice[{index}]").Trim();
            if (!VersionRegex.IsMatch(version) ||
                !DateTime.TryParseExact(version, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new InvalidOperationException($"公告记录错误：notice[{index}] version must be yyyyMMddHHmm");
            }

            var enabledToken = obj["enabled"];
            if (enabledToken == null || enabledToken.Type != JTokenType.Boolean)
            {
                throw new InvalidOperationException($"公告记录错误：notice[{index}] enabled must be boolean");
            }

            var textsToken = obj["texts"];
            if (textsToken == null || textsToken.Type != JTokenType.Object)
            {
                throw new InvalidOperationException($"公告记录错误：notice[{index}] texts must be an object");
            }

            var texts = new Dictionary<string, ResourceUpdateNoticeContent>(StringComparer.Ordinal);
            foreach (var property in ((JObject)textsToken).Properties())
            {
                var language = property.Name;
                ValidatePlainText(language, 64, $"notice[{index}].texts language");
                if (property.Value.Type != JTokenType.Object)
                {
                    throw new InvalidOperationException($"公告文本错误：notice[{index}].texts.{language} text must be an object");
                }

                texts[language] = ParseText((JObject)property.Value, version, $"notice[{index}].texts.{language}");
            }

            var record = new NoticeRecord(version, enabledToken.Value<bool>(), texts);
            if (record.Enabled && !record.TryGetDisplayable(defaultLanguage, out _))
            {
                throw new InvalidOperationException($"公告记录错误：notice[{index}] enabled record requires displayable default language text");
            }

            return record;
        }

        private static ResourceUpdateNoticeContent ParseText(JObject obj, string version, string stage)
        {
            var title = ReadOptionalString(obj, "title", stage, MaxTitleCodePoints);
            var summary = ReadOptionalString(obj, "summary", stage, MaxSummaryCodePoints);
            var items = new List<string>();
            var itemsToken = obj["items"];
            if (itemsToken != null)
            {
                if (itemsToken.Type != JTokenType.Array)
                {
                    throw new InvalidOperationException($"公告文本错误：{stage}.items must be an array");
                }

                var array = (JArray)itemsToken;
                if (array.Count > MaxItems)
                {
                    throw new InvalidOperationException($"公告文本错误：{stage}.items more than 20 items");
                }

                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i].Type != JTokenType.String)
                    {
                        throw new InvalidOperationException($"公告文本错误：{stage}.items[{i}] item must be string");
                    }

                    var item = array[i].Value<string>()?.Trim() ?? string.Empty;
                    ValidatePlainText(item, MaxItemCodePoints, $"{stage}.items[{i}]");
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        items.Add(item);
                    }
                }
            }

            return new ResourceUpdateNoticeContent(version, title.Trim(), summary.Trim(), items);
        }

        private static string ReadRequiredString(JObject obj, string name, string stage)
        {
            var token = obj[name];
            if (token == null || token.Type != JTokenType.String)
            {
                throw new InvalidOperationException($"公告字段错误：{stage}.{name} required string is missing");
            }

            var value = token.Value<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"公告字段错误：{stage}.{name} required string is empty");
            }

            return value;
        }

        private static string ReadOptionalString(JObject obj, string name, string stage, int maxCodePoints)
        {
            var token = obj[name];
            if (token == null)
            {
                return string.Empty;
            }

            if (token.Type != JTokenType.String)
            {
                throw new InvalidOperationException($"公告字段错误：{stage}.{name} value must be string");
            }

            var value = token.Value<string>() ?? string.Empty;
            ValidatePlainText(value, maxCodePoints, $"{stage}.{name}");
            return value;
        }

        private static void ValidatePlainText(string value, int maxCodePoints, string stage)
        {
            if (value.IndexOf('\uFEFF') >= 0)
            {
                throw new InvalidOperationException($"公告文本错误：{stage} BOM is not allowed");
            }

            foreach (var ch in value)
            {
                if (char.IsControl(ch))
                {
                    throw new InvalidOperationException($"公告文本错误：{stage} control characters are not allowed");
                }
            }

            if (RichTextRegex.IsMatch(value))
            {
                throw new InvalidOperationException($"公告文本错误：{stage} rich text tags are not allowed");
            }

            if (CountCodePoints(value) > maxCodePoints)
            {
                throw new InvalidOperationException($"公告文本错误：{stage} text is too long");
            }
        }

        private static int CountCodePoints(string value)
        {
            var count = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) &&
                    i + 1 < value.Length &&
                    char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// 保存单条公告记录的版本、启用状态和所有语言文本。
        /// </summary>
        public sealed class NoticeRecord
        {
            public NoticeRecord(
                string version,
                bool enabled,
                IReadOnlyDictionary<string, ResourceUpdateNoticeContent> texts)
            {
                Version = version;
                Enabled = enabled;
                Texts = texts;
            }

            public string Version { get; }
            public bool Enabled { get; }
            public IReadOnlyDictionary<string, ResourceUpdateNoticeContent> Texts { get; }

            public bool TryGetDisplayable(string languageCode, out ResourceUpdateNoticeContent content)
            {
                if (!string.IsNullOrWhiteSpace(languageCode) &&
                    Texts.TryGetValue(languageCode, out content) &&
                    content.IsDisplayable)
                {
                    return true;
                }

                content = default;
                return false;
            }
        }
    }
}
