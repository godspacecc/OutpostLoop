using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>
    /// 表示持久化存档主体，包含存档身份、时间戳、地图上下文和分区数据；克隆用于事务回滚隔离。
    /// </summary>
    public sealed class SaveGameData
    {
        public const int CurrentVersion = 1;
        public const string DefaultMapId = "map.outpost";

        public int Version { get; set; } = CurrentVersion;
        public string SaveId { get; set; }
        public string SaveName { get; set; }
        public string CreatedAtUtc { get; set; }
        public string UpdatedAtUtc { get; set; }
        public MapSaveContext MapContext { get; set; } = new MapSaveContext();
        public Dictionary<string, JToken> Sections { get; set; } = new Dictionary<string, JToken>(StringComparer.Ordinal);

        public SaveGameData Clone()
        {
            return new SaveGameData
            {
                Version = Version,
                SaveId = SaveId,
                SaveName = SaveName,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = UpdatedAtUtc,
                MapContext = MapContext?.Clone(),
                Sections = CloneSections(Sections)
            };
        }

        public static Dictionary<string, JToken> CloneSections(Dictionary<string, JToken> sections)
        {
            var clone = new Dictionary<string, JToken>(StringComparer.Ordinal);
            if (sections == null)
            {
                return clone;
            }

            foreach (var pair in sections)
            {
                clone[pair.Key] = pair.Value?.DeepClone();
            }

            return clone;
        }

        public static SaveGameData Create(string saveId, string saveName, string mapId, DateTime utcNow)
        {
            var timestamp = utcNow.ToUniversalTime().ToString("O");
            return new SaveGameData
            {
                Version = CurrentVersion,
                SaveId = saveId,
                SaveName = saveName,
                CreatedAtUtc = timestamp,
                UpdatedAtUtc = timestamp,
                MapContext = new MapSaveContext { MapId = string.IsNullOrWhiteSpace(mapId) ? DefaultMapId : mapId },
                Sections = new Dictionary<string, JToken>(StringComparer.Ordinal)
            };
        }
    }

    /// <summary>
    /// 保存存档对应的地图 ID 与地图内玩家位置等上下文，允许地图系统按需恢复场景状态。
    /// </summary>
    public sealed class MapSaveContext
    {
        public string MapId { get; set; } = SaveGameData.DefaultMapId;
        public int? Seed { get; set; }
        public int? GeneratorVersion { get; set; }

        public MapSaveContext Clone()
        {
            return new MapSaveContext
            {
                MapId = MapId,
                Seed = Seed,
                GeneratorVersion = GeneratorVersion
            };
        }
    }

    /// <summary>
    /// 表示存档列表中的轻量摘要，供 UI 展示和排序；不携带完整分区数据。
    /// </summary>
    public sealed class SaveSlotInfo
    {
        public string SaveId { get; set; }
        public string SaveName { get; set; }
        public string CreatedAtUtc { get; set; }
        public string UpdatedAtUtc { get; set; }
        public string MapId { get; set; }
        public bool IsCorrupted { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// 描述创建存档槽位的结果，成功时包含新存档，失败时仅提供诊断原因。
    /// </summary>
    public sealed class SaveCreateResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public SaveGameData Data { get; set; }
        public SaveSlotInfo Slot { get; set; }
    }

    /// <summary>
    /// 描述读取并迁移存档后的准备结果，数据在提交前可被流程失败安全丢弃。
    /// </summary>
    public sealed class SavePrepareResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public SaveGameData Data { get; set; }
        public SaveSlotInfo Slot { get; set; }
    }
}
