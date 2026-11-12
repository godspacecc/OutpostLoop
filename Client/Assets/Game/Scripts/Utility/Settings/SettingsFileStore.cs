using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 表示设置文件读取结果，区分成功、缺失、损坏和不支持版本，并保留诊断原因。
    /// </summary>
    public enum SettingsLoadResult
    {
        LoadedPrimary,
        LoadedBackup,
        CreatedDefault
    }

    /// <summary>
    /// 定义设置文件读取和原子写入边界，调用方不关心具体路径和备份策略。
    /// </summary>
    public interface ISettingsFileStore : IUtility
    {
        SettingsData LoadOrDefault(out SettingsLoadResult result);
        bool Save(SettingsData data, out string error);
    }

    /// <summary>
    /// 负责设置文件位置、JSON 读写、临时文件原子替换和损坏文件回退，不直接应用设置。
    /// </summary>
    public sealed class SettingsFileStore : ISettingsFileStore
    {
        private const string SettingsDirectoryName = "Settings";
        private const string SettingsFileName = "settings.json";
        private const string TempFileName = "settings.tmp.json";
        private const string BackupFileName = "settings.backup.json";

        private readonly string rootDirectory;

        public SettingsFileStore() : this(Application.persistentDataPath)
        {
        }

        public SettingsFileStore(string rootDirectory)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Application.persistentDataPath
                : rootDirectory;
        }

        public string SettingsDirectory => Path.Combine(rootDirectory, SettingsDirectoryName);
        public string SettingsPath => Path.Combine(SettingsDirectory, SettingsFileName);
        public string TempPath => Path.Combine(SettingsDirectory, TempFileName);
        public string BackupPath => Path.Combine(SettingsDirectory, BackupFileName);

        public SettingsData LoadOrDefault(out SettingsLoadResult result)
        {
            if (TryLoad(SettingsPath, out var primary, out _))
            {
                result = SettingsLoadResult.LoadedPrimary;
                return primary;
            }

            if (TryLoad(BackupPath, out var backup, out _))
            {
                result = SettingsLoadResult.LoadedBackup;
                return backup;
            }

            result = SettingsLoadResult.CreatedDefault;
            return SettingsData.CreateDefault();
        }

        public bool Save(SettingsData data, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                data = (data ?? SettingsData.CreateDefault()).Clone();
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(TempPath, json);

                if (!TryLoad(TempPath, out _, out error))
                {
                    TryDelete(TempPath);
                    return false;
                }

                if (File.Exists(SettingsPath))
                {
                    TryDelete(BackupPath);
                    File.Replace(TempPath, SettingsPath, BackupPath, true);
                }
                else
                {
                    File.Move(TempPath, SettingsPath);
                }

                return true;
            }
            catch (Exception e)
            {
                error = $"保存设置文件异常：{e.Message}";
                TryDelete(TempPath);
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static bool TryLoad(string path, out SettingsData data, out string error)
        {
            data = null;
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = "设置文件不存在";
                    return false;
                }

                var raw = JObject.Parse(File.ReadAllText(path));
                return TryMigrateToCurrent(raw, out data, out error);
            }
            catch (Exception e)
            {
                error = $"读取设置文件异常：{e.Message}";
                return false;
            }
        }

        private static bool TryMigrateToCurrent(JObject raw, out SettingsData data, out string error)
        {
            data = null;
            error = null;
            if (raw == null ||
                raw.TryGetValue("version", StringComparison.OrdinalIgnoreCase, out var versionToken) == false ||
                versionToken.Type != JTokenType.Integer)
            {
                error = "设置文件缺少版本号";
                return false;
            }

            var version = versionToken.Value<int>();
            if (version != SettingsData.CurrentVersion)
            {
                error = $"不支持的设置版本：{version}";
                return false;
            }

            data = raw.ToObject<SettingsData>()?.Sanitize();
            return data != null;
        }
    }
}
