using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>
    /// 按版本注册并串行执行存档 JSON 迁移；缺少迁移或遇到未来版本时停止并返回原因。
    /// </summary>
    public sealed class SaveMigrationRegistry
    {
        private readonly Dictionary<(int from, int to), Func<JObject, JObject>> migrations =
            new Dictionary<(int from, int to), Func<JObject, JObject>>();

        public void Register(int fromVersion, int toVersion, Func<JObject, JObject> migrate)
        {
            if (toVersion != fromVersion + 1)
            {
                throw new ArgumentException("存档迁移必须按单版本递增注册。");
            }

            migrations[(fromVersion, toVersion)] = migrate ?? throw new ArgumentNullException(nameof(migrate));
        }

        /// <summary>
        /// 按版本逐步迁移存档 JSON；缺少迁移步骤或遇到未来版本时停止并返回明确原因。
        /// </summary>
        public bool TryMigrate(JObject raw, out JObject migrated, out string error)
        {
            migrated = raw == null ? null : (JObject)raw.DeepClone();
            error = null;
            if (migrated == null ||
                migrated.TryGetValue("Version", StringComparison.OrdinalIgnoreCase, out var token) == false ||
                token.Type != JTokenType.Integer)
            {
                error = "缺少存档版本";
                return false;
            }

            var version = token.Value<int>();
            if (version > SaveGameData.CurrentVersion)
            {
                error = $"存档版本来自未来：{version}";
                return false;
            }

            while (version < SaveGameData.CurrentVersion)
            {
                if (!migrations.TryGetValue((version, version + 1), out var migrate))
                {
                    error = $"缺少存档迁移步骤：{version}->{version + 1}";
                    return false;
                }

                migrated = migrate(migrated);
                version++;
                migrated["Version"] = version;
            }

            return true;
        }
    }
}
