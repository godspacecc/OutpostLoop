using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>
    /// 封装运行时 JSON 序列化设置，统一项目存档、设置和工具数据的格式化规则。
    /// </summary>
    public static class RuntimeJson
    {
        public static bool TryParseStringTable(string json, out Dictionary<string, string> table)
        {
            table = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (parsed == null)
                {
                    return false;
                }

                table = new Dictionary<string, string>(parsed, StringComparer.Ordinal);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static bool TryParseObject(string json, out JObject root)
        {
            root = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                root = JObject.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static bool TryParseArray(string json, out JArray root)
        {
            root = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                root = JArray.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static string GetString(JToken token, string key)
        {
            return token?[key]?.Value<string>() ?? string.Empty;
        }
    }
}
