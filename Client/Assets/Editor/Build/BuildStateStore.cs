#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 支撑编辑器构建流程中的 BuildState，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    [Serializable]
    public sealed class BuildState
    {
        public string LastWindowsBaselineId;
        public string LastAndroidBaselineId;
        public HostBuildRecord LastHost;
        public AotBaselineRecord LastAotBaseline;
        public PackageBuildRecord LastPackage;
        public PlayerBuildRecord LastPlayer;
        public HostBuildRecord LastAndroidHost;
        public AotBaselineRecord LastAndroidAotBaseline;
        public PackageBuildRecord LastAndroidPackage;
        public PlayerBuildRecord LastAndroidPlayer;
    }

    /// <summary>
    /// 支撑编辑器构建流程中的 HostBuildRecord，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    [Serializable]
    public sealed class HostBuildRecord
    {
        public string BaselineId;
        public string Version;
        public string BuildTarget;
        public string OutputPackageDirectory;
        public string GameDllDirectory;
        public long CompletedUtcTicks;
    }

    /// <summary>
    /// 支撑编辑器构建流程中的 AotBaselineRecord，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    [Serializable]
    public sealed class AotBaselineRecord
    {
        public string BaselineId;
        public string HostAotDirectory;
        public string BuildTarget;
        public string AotDirectory;
        public string RuntimeListPath;
        public string GameDllDirectory;
        public long CompletedUtcTicks;
    }

    /// <summary>
    /// 支撑编辑器构建流程中的 PackageBuildRecord，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    [Serializable]
    public sealed class PackageBuildRecord
    {
        public string Version;
        public string BuildTarget;
        public string OutputPackageDirectory;
        public string BuildKind;
        public long CompletedUtcTicks;
    }

    /// <summary>
    /// 支撑编辑器构建流程中的 PlayerBuildRecord，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    [Serializable]
    public sealed class PlayerBuildRecord
    {
        public string PlayerPath;
        public string Version;
        public string BuildKind;
        public long CompletedUtcTicks;
    }

    /// <summary>
    /// 支撑编辑器构建流程中的 BuildStateStore，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    public static class BuildStateStore
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string StatePath
        {
            get
            {
                string projectPath = Directory.GetParent(Application.dataPath).FullName;
                return Path.Combine(projectPath, "Library", "OutpostLoopBuild", "BuildState.json");
            }
        }

        public static BuildState Load(out string warning)
        {
            warning = null;
            if (!File.Exists(StatePath))
                return new BuildState();

            try
            {
                var json = File.ReadAllText(StatePath, Utf8NoBom);
                var state = JsonUtility.FromJson<BuildState>(json);
                return state ?? new BuildState();
            }
            catch (Exception e)
            {
                warning = $"BuildState 无法读取，已忽略旧状态: {e.Message}";
                return new BuildState();
            }
        }

        public static void Save(BuildState state)
        {
            string dir = Path.GetDirectoryName(StatePath);
            Directory.CreateDirectory(dir);
            string temp = Path.Combine(dir, $"BuildState.{Guid.NewGuid():N}.tmp");
            string backup = Path.Combine(dir, "BuildState.json.bak");

            File.WriteAllText(temp, JsonUtility.ToJson(state, true), Utf8NoBom);
            if (File.Exists(StatePath))
                File.Replace(temp, StatePath, backup, true);
            else
                File.Move(temp, StatePath);

            if (File.Exists(temp))
                File.Delete(temp);
        }

        public static bool TryValidateRecentHost(BuildState state, BuildTarget target, out string error)
        {
            error = null;
            var host = state?.LastHost;
            if (host == null)
                return Fail("缺少最近 Host 成功记录，请先执行 Tools/打包/PC/Host代码+资源更新。", out error);
            if (!TargetMatches(host.BuildTarget, target))
                return Fail($"最近 Host 平台不匹配: {host.BuildTarget}，请改用完整打包。", out error);
            if (string.IsNullOrWhiteSpace(host.BaselineId) || host.BaselineId != state.LastAotBaseline?.BaselineId)
                return Fail("最近 Host 结果缺少可信宿主归属或与当前基线不一致，请重新执行指定基线的 Host 更新。", out error);

            if (string.IsNullOrEmpty(host.Version))
                return Fail("最近 Host 记录缺少版本，请重新执行 Host 更新。", out error);

            string outputDir = FullPath(host.OutputPackageDirectory);
            if (!Directory.Exists(outputDir))
                return Fail($"最近 Host 输出目录不存在: {outputDir}", out error);

            string versionFile = Path.Combine(outputDir, "OutpostLoop.version");
            if (!File.Exists(versionFile))
                return Fail($"最近 Host 缺少版本文件: {versionFile}", out error);

            string version = File.ReadAllText(versionFile).Trim();
            if (!string.Equals(version, host.Version, StringComparison.Ordinal))
                return Fail($"最近 Host 版本不一致: 记录 {host.Version}，文件 {version}", out error);

            string gameDllDir = FullPath(host.GameDllDirectory);
            return ValidateGameDllDirectory(gameDllDir, out error);
        }

        public static bool TryValidateAotBaseline(BuildState state, BuildTarget target, out string error)
        {
            return TryValidateAotBaselineRecord(state?.LastAotBaseline, target, out error);
        }

        /// <summary>校验 Android Host 基础包保存的 AOT 基线，只允许同平台热更新复用。</summary>
        public static bool TryValidateAndroidAotBaseline(BuildState state, BuildTarget target, out string error)
        {
            return TryValidateAotBaselineRecord(state?.LastAndroidAotBaseline, target, out error);
        }

        private static bool TryValidateAotBaselineRecord(
            AotBaselineRecord baseline,
            BuildTarget target,
            out string error)
        {
            error = null;
            if (baseline == null)
                return Fail("缺少 AOT 基线记录，请先执行完整打包。", out error);

            if (!TargetMatches(baseline.BuildTarget, target))
                return Fail($"AOT 基线平台不匹配: {baseline.BuildTarget}，请改用完整打包。", out error);

            string listPath = FullPath(baseline.RuntimeListPath);
            if (!File.Exists(listPath))
                return Fail($"AOT 基线清单不存在: {listPath}", out error);

            string aotDir = FullPath(baseline.AotDirectory);
            if (!Directory.Exists(aotDir))
                return Fail($"AOT 基线目录不存在: {aotDir}", out error);

            var entries = ReadRuntimeList(listPath).ToList();
            if (entries.Count == 0)
                return Fail($"AOT 基线清单为空: {listPath}", out error);

            foreach (string dllName in entries)
            {
                string path = Path.Combine(aotDir, NormalizeDllName(dllName));
                if (!File.Exists(path))
                    return Fail($"AOT 基线缺少 DLL: {path}", out error);
            }

            string gameDllDir = FullPath(baseline.GameDllDirectory);
            return ValidateGameDllDirectory(gameDllDir, out error);
        }

        public static IEnumerable<string> ReadRuntimeList(string listPath)
        {
            return File.ReadAllLines(listPath, Utf8NoBom)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line) && !line.StartsWith("//"))
                .Select(NormalizeDllName)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool ValidateGameDllDirectory(string gameDllDir, out string error)
        {
            if (!Directory.Exists(gameDllDir))
                return Fail($"DLL 目录不存在: {gameDllDir}", out error);

            string gameDll = Path.Combine(gameDllDir, "Game.dll.bytes");
            if (!File.Exists(gameDll))
                return Fail($"缺少热更新 DLL: {gameDll}", out error);

            string listPath = Path.Combine(gameDllDir, "HybridCLRAssemblyList.bytes");
            if (!File.Exists(listPath))
                return Fail($"缺少 AOT 运行清单: {listPath}", out error);

            var entries = ReadRuntimeList(listPath).ToList();
            if (entries.Count == 0)
                return Fail($"AOT 运行清单为空: {listPath}", out error);

            foreach (string dllName in entries)
            {
                string path = Path.Combine(gameDllDir, $"{NormalizeDllName(dllName)}.bytes");
                if (!File.Exists(path))
                    return Fail($"AOT bytes 缺失: {path}", out error);
            }

            error = null;
            return true;
        }

        private static string FullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            return Path.GetFullPath(path);
        }

        private static bool TargetMatches(string recorded, BuildTarget target)
        {
            return string.Equals(recorded, target.ToString(), StringComparison.Ordinal);
        }

        private static string NormalizeDllName(string dllName)
        {
            return dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? dllName : $"{dllName}.dll";
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
#endif
