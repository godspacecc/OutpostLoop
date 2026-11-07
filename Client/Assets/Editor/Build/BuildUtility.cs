#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.Editor
{
    /// <summary>
    /// 支撑编辑器构建流程中的 BuildUtility，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    public static class BuildUtility
    {
        private const string PackageName = "OutpostLoop";
        private const string BootScene = "Assets/Game/Scenes/Boot.unity";
        private const string BurstDebugInformationSuffix = "_BurstDebugInformation_DoNotShip";
        private const string Il2CppDebugInformationSuffix = "_BackUpThisFolder_ButDontShipItWithYourGame";
        private const string PendingWorkflowKey = "OutpostLoop.PCBuild.PendingWorkflow";
        private const string ReloadCompletedKey = "OutpostLoop.PCBuild.ReloadCompleted";
        private static bool s_isBuilding;

        internal static bool HasActiveWorkflow => s_isBuilding ||
            !string.IsNullOrEmpty(SessionState.GetString(PendingWorkflowKey, ""));

        [MenuItem("Tools/打包/PC/完整打包", false, 100)]
        private static void BuildLatestMenu()
        {
            if (Confirm("PC 最新代码资源完整打包", "重新生成 HybridCLR、AOT、Game DLL 和完整资源，Player 内置完整资源，首次启动无需下载基础资源；耗时最长；正式发布或无法判断时使用。", "开始完整打包"))
                BuildLatestCodeAndResourcesBatch();
        }

        [MenuItem("Tools/打包/PC/快速打包/复用全部打包", false, 200)]
        private static void BuildRecentHostMenu()
        {
            if (Confirm("使用最近 Host 结果打包", "复用最近 Host 结果；请确认 Host 成功后未修改项目；将生成完整内置资源和 Player。", "使用 Host 结果打包"))
                BuildRecentHostPlayerBatch();
        }

        [MenuItem("Tools/打包/PC/快速打包/复用 AOT 打包", false, 201)]
        private static void BuildQuickAotMenu()
        {
            if (Confirm("复用 AOT 快速打包", "重新编译 Game 和资源但复用 AOT；仅用于临时验证；不能替代发布前完整打包。", "开始快速打包"))
                BuildQuickAotPlayerBatch();
        }

        [MenuItem("Tools/打包/PC/构建热更新（代码+资源）", false, 101)]
        private static void BuildHostMenu()
        {
            BuildHostCodeAndResourcesBatch();
        }

        [MenuItem("Tools/打包/PC/高级/仅重建完整内置资源", false, 202)]
        private static void BuildBuiltinResourcesOnlyMenu()
        {
            BuildBuiltinResourcesOnlyBatch();
        }

        [MenuItem("Tools/服务器/PC/启动资源服", false, 200)]
        private static void StartResourceServerMenu()
        {
            StartResourceServerFromLatestPackage();
        }

        [MenuItem("Tools/服务器/PC/生成资源服", false, 201)]
        private static void PublishResourceServerMenu()
        {
            var result = ResourceServerPublishUtility.PublishLatestValidPackage();
            ShowResourceServerPublishResult(result);
        }

        [MenuItem("Tools/服务器/PC/更新资源服", false, 202)]
        private static void UpdateResourceServerMenu()
        {
            var result = ResourceServerPublishUtility.UpdateLatestBundle();
            ShowResourceServerPublishResult(result);
        }

        public static void BuildLatestCodeAndResourcesBatch()
        {
            BuildLatestCodeAndResources(true);
        }

        /// <summary>只生成本次产物，不发布、不启动资源服；要求平台已选定。</summary>
        public static BuildArtifacts BuildLatestCodeAndResourcesOnly()
        {
            BuildArtifacts.RequireReady(BuildTarget.StandaloneWindows64);
            return BuildLatestCodeAndResources(false);
        }

        private static BuildArtifacts BuildLatestCodeAndResources(bool publish)
        {
            BuildArtifacts artifacts = null;
            RunWorkflow("最新代码资源完整打包", timing =>
            {
                string compiler = WindowsInstallerPublishUtility.EnsureCompiler();
                ValidateProjectSettings();
                BuildTarget target = BuildTarget.StandaloneWindows64;
                var hostInputs = timing.Run("CaptureHostInputs", HostBaselineUtility.Capture);

                var copied = HybridCLRPreparation.Run(target, timing, () => { BuildPreparatoryPlayer(); });

                string version = CreateVersion();
                var package = timing.Run("BuildOutpostLoop", () => BuildPackageOrThrow(version, true));
                timing.Run("ValidateBuiltinPackage", () => ValidatePackageOrThrow(package.OutputPackageDirectory, version));
                timing.Run("CleanupPreparatoryPlayer", CleanupPreparatoryPlayer);
                using var releaseIdentity = new WindowsReleaseIdentity();
                string player = timing.Run("BuildWindowsPlayer", BuildWindowsPlayer);
                timing.Run("ValidatePlayerAndBuiltin", () => ValidatePlayerAndBuiltin(player, version));
                CiBuildProgress.Artifact("Player", Path.GetDirectoryName(player));
                releaseIdentity.ValidatePlayer(Path.GetDirectoryName(player));
                releaseIdentity.Commit();
                string installerDirectory = timing.Run("GenerateInstaller", () => WindowsInstallerPublishUtility.GenerateInstaller(player, compiler));
                artifacts = CreatePlayerArtifacts(player, version, package.OutputPackageDirectory, installerDirectory);

                SaveSuccessfulState(version, "Full", package.OutputPackageDirectory, player, updateAot: true, updateHost: false);
                timing.Run("ArchiveHostBaseline", () => HostBaselineUtility.Archive(artifacts, hostInputs));
                Debug.Log($"[BuildUtility] 完整打包完成：Version={version}，Player={player}，DLLs={copied.Count}");
                if (publish)
                {
                    var published = timing.Run("PublishArtifacts", () => artifacts.Publish());
                    OfferStartServer(version, Path.Combine(published.BundleDirectory, "ResourceFiles"), "打包成功");
                }
                else
                    Debug.Log($"[BuildUtility] 只构建完成，安装器及清单：{installerDirectory}");
            }, cleanupPrepPlayer: true);
            return artifacts;
        }

        public static void BuildHostCodeAndResourcesBatch()
        {
            BuildHostCodeAndResources(true);
        }

        /// <summary>只生成本次产物，不发布、不启动资源服；要求平台已选定。</summary>
        public static BuildArtifacts BuildHostCodeAndResourcesOnly()
        {
            BuildArtifacts.RequireReady(BuildTarget.StandaloneWindows64);
            return BuildHostCodeAndResources(false);
        }

        public static BuildArtifacts BuildHostCodeAndResourcesFromBaselineOnly(string baselineId)
        {
            BuildArtifacts.RequireReady(BuildTarget.StandaloneWindows64);
            if (string.IsNullOrWhiteSpace(baselineId)) throw new ArgumentException("必须选择宿主基线。", nameof(baselineId));
            return BuildHostCodeAndResources(false, baselineId);
        }

        private static BuildArtifacts BuildHostCodeAndResources(bool publish, string baselineId = null)
        {
            BuildArtifacts artifacts = null;
            RunWorkflow("Host代码+资源更新", timing =>
            {
                ValidateProjectSettings();
                BuildTarget target = BuildTarget.StandaloneWindows64;

                var baseline = timing.Run("RestoreHostBaseline", () => HostBaselineUtility.Restore(target, baselineId));
                var baselineState = LoadStateOrThrow();
                timing.Run("CompileHotUpdateDll", () => HybridCLRBuildUtility.CompileHotUpdateDll(target));
                timing.Run("ValidateHotUpdateAgainstBaseline", () => HybridCLRBuildUtility.ValidateHotUpdateAgainstBaseline(
                    target, baselineState.LastAotBaseline, baseline));
                var copied = timing.Run("CopyGeneratedDllsFromBaseline", () => HybridCLRBuildUtility.CopyGeneratedDllsFromBaseline(
                    target, baselineState.LastAotBaseline.AotDirectory, baselineState.LastAotBaseline.RuntimeListPath));
                timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);

                string builtinDir = YooAssetBuildUtility.GetStreamingAssetsDirectory();
                var before = timing.Run("SnapshotBuiltinBefore", () => SnapshotDirectory(builtinDir));

                string version = CreateVersionAfter(baseline.ResourceVersion);
                var package = timing.Run("BuildHostPackage", () => BuildPackageOrThrow(version, false));
                timing.Run("ValidateHostPackage", () => ValidatePackageOrThrow(package.OutputPackageDirectory, version));
                var after = timing.Run("SnapshotBuiltinAfter", () => SnapshotDirectory(builtinDir));
                timing.Run("CompareBuiltinSnapshot", () => EnsureSnapshotsEqual(before, after));

                SaveSuccessfulState(version, "Host", package.OutputPackageDirectory, null, updateAot: false, updateHost: true);
                artifacts = new BuildArtifacts
                {
                    Target = BuildTarget.StandaloneWindows64, ResourceVersion = version,
                    PackageDirectory = package.OutputPackageDirectory, HostBaselineId = baseline.Id,
                    AppVersion = baseline.AppVersion, AppVersionCode = baseline.AppVersionCode
                };
                if (!publish)
                {
                    Debug.Log($"[BuildUtility] Host 只构建完成：{package.OutputPackageDirectory}");
                    return;
                }
                var server = timing.Run("StartResourceServer", () => LocalResourceServerUtility.StartAndProbe(package.OutputPackageDirectory, version));
                if (!server.Success)
                {
                    ShowDialog("Host 资源构建成功，资源服启动失败", $"版本: {version}\n资源目录: {package.OutputPackageDirectory}\n失败原因: {server.Error}");
                    return;
                }

                Debug.Log($"[BuildUtility] Host 更新完成：Version={version}，DLLs={copied.Count}，Server={server.BaseUrl}");
                ShowDialog("Host 更新成功", $"版本: {version}\n资源目录: {package.OutputPackageDirectory}\n资源服: {server.BaseUrl}");
            }, cleanupPrepPlayer: false);
            return artifacts;
        }

        public static void BuildBuiltinResourcesOnlyBatch()
        {
            RunWorkflow("仅重建完整内置资源", timing =>
            {
                ValidateProjectSettings();
                string version = CreateVersion();
                var package = timing.Run("BuildOutpostLoop", () => BuildPackageOrThrow(version, true));
                timing.Run("ValidateOutputPackage", () => ValidatePackageOrThrow(package.OutputPackageDirectory, version));
                string builtinDir = YooAssetBuildUtility.GetStreamingAssetsDirectory();
                timing.Run("ValidateStreamingAssetsPackage", () => ValidatePackageOrThrow(builtinDir, version));
                Debug.Log($"[BuildUtility] 仅重建完整内置资源完成：Version={version}, Output={package.OutputPackageDirectory}, StreamingAssets={builtinDir}");
            }, cleanupPrepPlayer: false);
        }

        public static void BuildRecentHostPlayerBatch()
        {
            BuildRecentHostPlayer(true);
        }

        /// <summary>只生成本次产物，不发布、不启动资源服；要求平台已选定。</summary>
        public static BuildArtifacts BuildRecentHostPlayerOnly()
        {
            BuildArtifacts.RequireReady(BuildTarget.StandaloneWindows64);
            return BuildRecentHostPlayer(false);
        }

        private static BuildArtifacts BuildRecentHostPlayer(bool publish)
        {
            BuildArtifacts artifacts = null;
            RunWorkflow("使用最近 Host 结果打包", timing =>
            {
                string compiler = WindowsInstallerPublishUtility.EnsureCompiler();
                ValidateProjectSettings();
                var state = LoadStateOrThrow();
                timing.Run("ValidateLastHost", () =>
                {
                    if (!BuildStateStore.TryValidateRecentHost(state, BuildTarget.StandaloneWindows64, out string error))
                        throw new InvalidOperationException($"最近 Host 校验失败：{error}");
                });

                string version = CreateVersionAfter(state.LastHost.Version);
                var package = timing.Run("BuildOutpostLoop", () => BuildPackageOrThrow(version, true));
                timing.Run("ValidateBuiltinPackage", () => ValidatePackageOrThrow(package.OutputPackageDirectory, version));
                using var releaseIdentity = new WindowsReleaseIdentity();
                string player = timing.Run("BuildWindowsPlayer", BuildWindowsPlayer);
                timing.Run("ValidatePlayerAndBuiltin", () => ValidatePlayerAndBuiltin(player, version));
                CiBuildProgress.Artifact("Player", Path.GetDirectoryName(player));
                releaseIdentity.ValidatePlayer(Path.GetDirectoryName(player));
                releaseIdentity.Commit();
                string installerDirectory = timing.Run("GenerateInstaller", () => WindowsInstallerPublishUtility.GenerateInstaller(player, compiler));
                artifacts = CreatePlayerArtifacts(player, version, package.OutputPackageDirectory, installerDirectory);

                state.LastPackage = NewPackageRecord(version, "RecentHost", package.OutputPackageDirectory);
                state.LastPlayer = NewPlayerRecord(version, "RecentHost", player);
                BuildStateStore.Save(state);
                if (publish)
                {
                    var published = timing.Run("PublishArtifacts", () => artifacts.Publish());
                    OfferStartServer(version, Path.Combine(published.BundleDirectory, "ResourceFiles"), "打包成功");
                }
                else
                    Debug.Log($"[BuildUtility] 只构建完成，安装器及清单：{installerDirectory}");
            }, cleanupPrepPlayer: false);
            return artifacts;
        }

        public static void BuildQuickAotPlayerBatch()
        {
            BuildQuickAotPlayer(true);
        }

        /// <summary>只生成本次产物，不发布、不启动资源服；要求平台已选定。</summary>
        public static BuildArtifacts BuildQuickAotPlayerOnly()
        {
            BuildArtifacts.RequireReady(BuildTarget.StandaloneWindows64);
            return BuildQuickAotPlayer(false);
        }

        private static BuildArtifacts BuildQuickAotPlayer(bool publish)
        {
            BuildArtifacts artifacts = null;
            RunWorkflow("复用 AOT 快速打包", timing =>
            {
                string compiler = WindowsInstallerPublishUtility.EnsureCompiler();
                ValidateProjectSettings();
                var state = LoadStateOrThrow();
                timing.Run("ValidateAotBaseline", () =>
                {
                    if (!BuildStateStore.TryValidateAotBaseline(state, BuildTarget.StandaloneWindows64, out string error))
                        throw new InvalidOperationException($"AOT 基线校验失败：{error}");
                });

                BuildTarget target = BuildTarget.StandaloneWindows64;
                timing.Run("CompileHotUpdateDll", () => HybridCLRBuildUtility.CompileHotUpdateDll(target));
                var copied = timing.Run("CopyGeneratedDllsFromBaseline", () => HybridCLRBuildUtility.CopyGeneratedDllsFromBaseline(target, state.LastAotBaseline.AotDirectory, state.LastAotBaseline.RuntimeListPath));
                timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);

                string version = CreateVersion();
                var package = timing.Run("BuildOutpostLoop", () => BuildPackageOrThrow(version, true));
                timing.Run("ValidateBuiltinPackage", () => ValidatePackageOrThrow(package.OutputPackageDirectory, version));
                using var releaseIdentity = new WindowsReleaseIdentity();
                string player = timing.Run("BuildWindowsPlayer", BuildWindowsPlayer);
                timing.Run("ValidatePlayerAndBuiltin", () => ValidatePlayerAndBuiltin(player, version));
                CiBuildProgress.Artifact("Player", Path.GetDirectoryName(player));
                releaseIdentity.ValidatePlayer(Path.GetDirectoryName(player));
                releaseIdentity.Commit();
                string installerDirectory = timing.Run("GenerateInstaller", () => WindowsInstallerPublishUtility.GenerateInstaller(player, compiler));
                artifacts = CreatePlayerArtifacts(player, version, package.OutputPackageDirectory, installerDirectory);

                state.LastPackage = NewPackageRecord(version, "QuickAot", package.OutputPackageDirectory);
                state.LastPlayer = NewPlayerRecord(version, "QuickAot", player);
                BuildStateStore.Save(state);
                Debug.Log($"[BuildUtility] 复用 AOT 快速打包完成：DLLs={copied.Count}");
                if (publish)
                {
                    var published = timing.Run("PublishArtifacts", () => artifacts.Publish());
                    OfferStartServer(version, Path.Combine(published.BundleDirectory, "ResourceFiles"), "打包成功");
                }
                else
                    Debug.Log($"[BuildUtility] 只构建完成，安装器及清单：{installerDirectory}");
            }, cleanupPrepPlayer: false);
            return artifacts;
        }

        private static BuildArtifacts CreatePlayerArtifacts(string player, string version, string package, string installer)
        {
            var identity = UnityEngine.JsonUtility.FromJson<Boot.WindowsUpdateManifest>(File.ReadAllText(
                Path.Combine(Path.GetDirectoryName(player), "OutpostLoop_Data", "StreamingAssets", "windows-release.json")));
            return new BuildArtifacts
            {
                Target = BuildTarget.StandaloneWindows64, ResourceVersion = version,
                PackageDirectory = package, PlayerPath = player, InstallerDirectory = installer,
                UpdateManifestPath = Path.Combine(installer, "app-update-windows.json"),
                AppVersion = identity.versionName, AppVersionCode = identity.releaseNumber
            };
        }

        public static void StartResourceServerFromLatestPackage()
        {
            try
            {
                string packageRoot = YooAssetBuildUtility.GetPackageRoot(BuildTarget.StandaloneWindows64);
                if (!YooAssetBuildUtility.TryFindLatestValidPackage(
                        packageRoot,
                        out string dir,
                        out string version,
                        out string error))
                    throw new InvalidOperationException($"查找最新资源包失败：{error}");

                var result = LocalResourceServerUtility.StartAndProbe(dir, version);
                if (!result.Success)
                    throw new InvalidOperationException($"资源服启动失败：{result.Error}");

                ShowDialog("资源服已启动", $"版本: {version}\n资源目录: {dir}\n地址: {result.BaseUrl}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BuildUtility] 启动资源服失败：{e.Message}");
                ShowDialog("资源服启动失败", e.Message);
            }
        }

        internal static Dictionary<string, string> SnapshotDirectory(string directory)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"快照目录不存在：{directory}");

            var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(directory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(file))
                    snapshot[relative] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
            return snapshot;
        }

        /// <summary>
        /// 统一执行四个打包工作流，期间强制非 Development，并在成功、失败和异常路径恢复用户原设置。
        /// </summary>
        private static void RunWorkflow(string workflowName, Action<BuildStageTiming> action, bool cleanupPrepPlayer)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(Application.dataPath).FullName);
            if (!BeginBuild())
                return;

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                try
                {
                    SessionState.SetString(PendingWorkflowKey, workflowName);
                    SessionState.SetBool(ReloadCompletedKey, false);
                    EnsureWindowsBuildTarget();
                    Debug.Log($"[BuildUtility] 已暂停 {workflowName}，等待 Windows 编辑器程序集重载后自动继续。");
                }
                catch
                {
                    ClearPendingWorkflow();
                    throw;
                }
                finally
                {
                    s_isBuilding = false;
                }
                return;
            }

            var previousDevelopment = EditorUserBuildSettings.development;
            var timing = new BuildStageTiming(workflowName);
            try
            {
                timing.Run("EnsureWindowsBuildTarget", EnsureWindowsBuildTarget);
                EditorUserBuildSettings.development = false;
                action(timing);
                Debug.Log($"[BuildUtility] {workflowName} 已完成\n{timing.BuildSummary()}");
            }
            catch (Exception e)
            {
                if (cleanupPrepPlayer)
                    CleanupPreparatoryPlayer();
                Debug.LogError($"[BuildUtility] {workflowName} 在阶段 {timing.CurrentStage} 失败：{e.Message}\n{timing.BuildSummary()}");
                ShowDialog("构建失败", BuildWorkflowUtility.FormatFailure(timing, e.Message));
                throw;
            }
            finally
            {
                EditorUserBuildSettings.development = previousDevelopment;
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        private static bool BeginBuild()
        {
            if (HasActiveWorkflow || AndroidBuildUtility.HasActiveWorkflow)
            {
                Debug.LogWarning("[BuildUtility] 构建流程正在执行，请勿重复触发。");
                return false;
            }
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Unity 正在编译，请稍后再试。");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Unity 正在进入或退出 PlayMode，请稍后再试。");

            s_isBuilding = true;
            return true;
        }

        [InitializeOnLoadMethod]
        private static void ObservePendingWorkflow()
        {
            EditorApplication.update -= ResumePendingWorkflow;
            EditorApplication.update += ResumePendingWorkflow;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingWorkflowKey, "")))
                SessionState.SetBool(ReloadCompletedKey, true);
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (string.IsNullOrEmpty(SessionState.GetString(PendingWorkflowKey, "")) ||
                !messages.Any(message => message.type == CompilerMessageType.Error))
                return;

            ClearPendingWorkflow();
            Debug.LogError($"[BuildUtility] 平台切换后脚本编译失败，已取消待恢复的 PC 打包：{assemblyPath}");
        }

        private static void ResumePendingWorkflow()
        {
            string workflow = SessionState.GetString(PendingWorkflowKey, "");
            if (string.IsNullOrEmpty(workflow))
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ClearPendingWorkflow();
                Debug.LogWarning("[BuildUtility] 已进入 PlayMode，取消待恢复的 PC 打包。");
                return;
            }

            if (!BuildWorkflowUtility.CanResume(
                    SessionState.GetBool(ReloadCompletedKey, false),
                    EditorApplication.isCompiling,
                    EditorApplication.isUpdating))
                return;

            ClearPendingWorkflow();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                Debug.LogError("[BuildUtility] 重载后目标不是 Windows，已取消 PC 打包。");
                return;
            }

            try
            {
                switch (workflow)
                {
                    case "最新代码资源完整打包": BuildLatestCodeAndResourcesBatch(); break;
                    case "Host代码+资源更新": BuildHostCodeAndResourcesBatch(); break;
                    case "使用最近 Host 结果打包": BuildRecentHostPlayerBatch(); break;
                    case "复用 AOT 快速打包": BuildQuickAotPlayerBatch(); break;
                    case "仅重建完整内置资源": BuildBuiltinResourcesOnlyBatch(); break;
                    default: Debug.LogError($"[BuildUtility] 无法恢复未知工作流：{workflow}"); break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void ClearPendingWorkflow()
        {
            SessionState.EraseString(PendingWorkflowKey);
            SessionState.EraseBool(ReloadCompletedKey);
        }

        private static void EnsureWindowsBuildTarget()
        {
            const BuildTarget target = BuildTarget.StandaloneWindows64;
            if (EditorUserBuildSettings.activeBuildTarget == target)
                return;

            if (Application.isBatchMode)
                throw new InvalidOperationException(
                    $"批处理模式不能自动切换 Build Target，请使用 -buildTarget Win64 启动 Unity；当前为 {EditorUserBuildSettings.activeBuildTarget}。");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new InvalidOperationException("当前 Unity 未安装可用的 Windows Build Support。");

            BuildTarget previous = EditorUserBuildSettings.activeBuildTarget;
            Debug.Log($"[BuildUtility] 正在自动切换 Build Target：{previous} -> {target}");
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target);
            if (!switched || EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException($"Build Target 自动切换失败：{previous} -> {target}");
        }

        private static void ValidateProjectSettings()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException($"Build Target 必须是 StandaloneWindows64，当前为 {EditorUserBuildSettings.activeBuildTarget}");
            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone) != ScriptingImplementation.IL2CPP)
                throw new InvalidOperationException("Standalone 脚本后端必须是 IL2CPP。");

            ValidateInsecureHttpOption(PlayerSettings.insecureHttpOption);
            WindowsReleaseIdentity.ValidateSourceIdentity();

            var enabledScenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            if (enabledScenes.Length != 1 || enabledScenes[0].path != BootScene)
                throw new InvalidOperationException($"Build Settings 必须只启用 {BootScene}。");

            RequireFile("Assets/StreamingAssets/hostConfig.json");
            RequireDirectory("Assets/Game/Config");
            RequireDirectory("Assets/Game/Dlls");
            RequireFile("Assets/Editor/YooAsset/LocalServer/start_yooasset_local_server.ps1");
        }

        private static void ValidateInsecureHttpOption(InsecureHttpOption option)
        {
            if (option == InsecureHttpOption.AlwaysAllowed)
            {
                return;
            }

            throw new InvalidOperationException(
                $"非 Development Windows Player 需要 PlayerSettings.insecureHttpOption 为 AlwaysAllowed，当前为 {option}。");
        }

        private static string BuildPreparatoryPlayer()
        {
            ConfigureStandalonePlayerBuild();
            string tempPath = GetPreparatoryPlayerBuildPath();
            string exePath = Path.Combine(tempPath, "OutpostLoop.exe");
            DeleteDirectory(tempPath);
            Directory.CreateDirectory(tempPath);
            BuildPlayer(exePath);
            return exePath;
        }

        private static void CleanupPreparatoryPlayer()
        {
            DeleteDirectory(GetPreparatoryPlayerBuildPath());
        }

        private static string BuildWindowsPlayer()
        {
            ConfigureStandalonePlayerBuild();
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            string outputRoot = Path.Combine(projectPath, "Build", "Windows");
            string batchDirectory = GetWindowsPlayerBatchDirectory(outputRoot, DateTime.Now);
            if (Directory.Exists(batchDirectory) || File.Exists(batchDirectory))
                throw new InvalidOperationException($"Windows Player 发布批次已存在，拒绝覆盖：{batchDirectory}");

            string playerDirectory = Path.Combine(batchDirectory, "OutpostLoop");
            Directory.CreateDirectory(playerDirectory);
            string exePath = Path.Combine(playerDirectory, "OutpostLoop.exe");
            BuildPlayer(exePath);
            string symbolsDirectory = SeparateWindowsPlayerDebugSymbols(playerDirectory);
            Debug.Log($"[BuildUtility] Windows Player 输出完成：玩家目录={playerDirectory}，调试资料目录={symbolsDirectory}");
            return exePath;
        }

        private static string GetWindowsPlayerBatchDirectory(string outputRoot, DateTime timestamp)
        {
            return Path.GetFullPath(Path.Combine(outputRoot, timestamp.ToString("yyyyMMdd_HHmmss")));
        }

        private static string SeparateWindowsPlayerDebugSymbols(string playerDirectory)
        {
            string normalizedPlayerDirectory = Path.GetFullPath(playerDirectory);
            string batchDirectory = Directory.GetParent(normalizedPlayerDirectory)?.FullName;
            if (string.IsNullOrEmpty(batchDirectory))
                throw new InvalidOperationException($"无法确定 Windows Player 批次目录：{normalizedPlayerDirectory}");
            if (!Directory.Exists(normalizedPlayerDirectory))
                throw new DirectoryNotFoundException($"Windows Player 目录不存在，需要人工检查本批目录：{batchDirectory}");

            string burstDirectory = FindOptionalDebugDirectory(normalizedPlayerDirectory, BurstDebugInformationSuffix, batchDirectory);
            string il2CppDirectory = FindSingleDebugDirectory(normalizedPlayerDirectory, Il2CppDebugInformationSuffix, batchDirectory);
            string symbolsDirectory = Path.Combine(batchDirectory, "Symbols");
            string burstTarget = burstDirectory == null ? null : Path.Combine(symbolsDirectory, Path.GetFileName(burstDirectory));
            string il2CppTarget = Path.Combine(symbolsDirectory, Path.GetFileName(il2CppDirectory));
            if (Directory.Exists(symbolsDirectory) || File.Exists(symbolsDirectory))
                throw new InvalidOperationException($"调试资料目录已存在，拒绝覆盖：{symbolsDirectory}。请人工检查本批目录：{batchDirectory}");
            if (burstTarget != null && (Directory.Exists(burstTarget) || File.Exists(burstTarget)) ||
                Directory.Exists(il2CppTarget) || File.Exists(il2CppTarget))
                throw new InvalidOperationException($"调试资料目标目录已存在。请人工检查本批目录：{batchDirectory}");

            try
            {
                Directory.CreateDirectory(symbolsDirectory);
                if (burstDirectory != null)
                    Directory.Move(burstDirectory, burstTarget);
                Directory.Move(il2CppDirectory, il2CppTarget);
                if (burstDirectory == null)
                    Debug.LogWarning($"[BuildUtility] 本次 Player 未生成 Burst 调试资料目录，按 script-only build 产物继续：{batchDirectory}");
                return symbolsDirectory;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"调试资料分离失败，请人工检查本批目录：{batchDirectory}", e);
            }
        }

        private static string FindSingleDebugDirectory(string playerDirectory, string suffix, string batchDirectory)
        {
            string[] matches = Directory.GetDirectories(playerDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(directory => Path.GetFileName(directory).EndsWith(suffix, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"调试资料目录应唯一匹配后缀 {suffix}，实际为 {matches.Length} 个。请人工检查本批目录：{batchDirectory}");
            return matches[0];
        }

        private static string FindOptionalDebugDirectory(string playerDirectory, string suffix, string batchDirectory)
        {
            string[] matches = Directory.GetDirectories(playerDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(directory => Path.GetFileName(directory).EndsWith(suffix, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException($"调试资料目录最多匹配一个后缀 {suffix}，实际为 {matches.Length} 个。请人工检查本批目录：{batchDirectory}");
            return matches.Length == 0 ? null : matches[0];
        }

        /// <summary>
        /// 生成 Windows Player；诊断正式版固定使用 BuildOptions.None，不打开 Development、Profiler 或脚本调试。
        /// </summary>
        private static void BuildPlayer(string exePath)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { BootScene },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report == null)
                throw new InvalidOperationException($"Player 构建未返回报告，请查看 Unity Console 获取底层错误。输出路径: {exePath}");

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Player 构建失败: {summary.result}");
            string assemblyList = Path.Combine(
                Path.GetDirectoryName(exePath),
                Path.GetFileNameWithoutExtension(exePath) + "_Data",
                "ScriptingAssemblies.json");
            RequireFile(assemblyList);
            if (!WindowsBuildSafety.TryValidateAssemblyList(
                    File.ReadAllText(assemblyList),
                    HybridCLR.Editor.SettingsUtil.HotUpdateAssemblyFilesIncludePreserved,
                    out string error))
                throw new InvalidOperationException($"Player 热更程序集清单校验失败：{assemblyList}；{error}");
            Debug.Log($"[BuildUtility] Player 构建成功：{exePath}，大小={summary.totalSize}");
        }

        private static void ConfigureStandalonePlayerBuild()
        {
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "CreateSolution", "false");
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "CreateSolutionBuild", "false");
        }

        private static YooAssetBuildUtility.BuildResult BuildPackageOrThrow(string version, bool fullBuiltin)
        {
            var result = YooAssetBuildUtility.BuildOutpostLoop(version, fullBuiltin);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "YooAsset 构建失败。");
            CiBuildProgress.Artifact("Resources", result.OutputPackageDirectory);
            return result;
        }

        private static void ValidatePackageOrThrow(string directory, string version)
        {
            if (!YooAssetBuildUtility.ValidateBuildResult(directory, version))
                throw new InvalidOperationException($"资源包校验失败: {directory}, version={version}");
        }

        private static void ValidatePlayerAndBuiltin(string playerPath, string version)
        {
            if (!File.Exists(playerPath))
                throw new FileNotFoundException($"Player 不存在: {playerPath}");
            string builtinDirectory = YooAssetBuildUtility.GetStreamingAssetsDirectory();
            ValidatePackageOrThrow(builtinDirectory, version);
            RequireFile(Path.Combine(builtinDirectory, "BuildinCatalog.bytes"));
            RequireFile(Path.Combine(builtinDirectory, "BuildinCatalog.json"));
        }

        private static void ValidateExistingPlayerAot()
        {
            string aotDir = HybridCLRBuildUtility.GetAotDirectory(BuildTarget.StandaloneWindows64);
            if (!Directory.Exists(aotDir))
                throw new DirectoryNotFoundException($"Player AOT 输出目录不存在: {aotDir}");
            if (Directory.GetFiles(aotDir, "*.dll").Length == 0)
                throw new InvalidOperationException($"Player AOT 输出为空: {aotDir}");
        }

        private static void SaveSuccessfulState(string version, string buildKind, string packageDirectory, string playerPath, bool updateAot, bool updateHost)
        {
            var state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);

            if (updateHost)
            {
                state.LastHost = new HostBuildRecord
                {
                    BaselineId = state.LastAotBaseline?.BaselineId,
                    Version = version,
                    BuildTarget = BuildTarget.StandaloneWindows64.ToString(),
                    OutputPackageDirectory = Path.GetFullPath(packageDirectory),
                    GameDllDirectory = HybridCLRBuildUtility.GetGameDllDirectory(),
                    CompletedUtcTicks = DateTime.UtcNow.Ticks
                };
            }

            if (updateAot)
            {
                state.LastAotBaseline = new AotBaselineRecord
                {
                    BuildTarget = BuildTarget.StandaloneWindows64.ToString(),
                    AotDirectory = HybridCLRBuildUtility.GetAotDirectory(BuildTarget.StandaloneWindows64),
                    RuntimeListPath = HybridCLRBuildUtility.GetRuntimeListPath(),
                    GameDllDirectory = HybridCLRBuildUtility.GetGameDllDirectory(),
                    CompletedUtcTicks = DateTime.UtcNow.Ticks
                };
            }

            state.LastPackage = NewPackageRecord(version, buildKind, packageDirectory);
            if (!string.IsNullOrEmpty(playerPath))
                state.LastPlayer = NewPlayerRecord(version, buildKind, playerPath);
            BuildStateStore.Save(state);
        }

        private static PackageBuildRecord NewPackageRecord(string version, string buildKind, string outputPackageDirectory)
        {
            return new PackageBuildRecord
            {
                Version = version,
                BuildTarget = BuildTarget.StandaloneWindows64.ToString(),
                OutputPackageDirectory = Path.GetFullPath(outputPackageDirectory),
                BuildKind = buildKind,
                CompletedUtcTicks = DateTime.UtcNow.Ticks
            };
        }

        private static PlayerBuildRecord NewPlayerRecord(string version, string buildKind, string playerPath)
        {
            return new PlayerBuildRecord
            {
                PlayerPath = Path.GetFullPath(playerPath),
                Version = version,
                BuildKind = buildKind,
                CompletedUtcTicks = DateTime.UtcNow.Ticks
            };
        }

        private static BuildState LoadStateOrThrow()
        {
            var state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);
            return state;
        }

        private static void EnsureSnapshotsEqual(Dictionary<string, string> before, Dictionary<string, string> after)
        {
            if (before.Count != after.Count)
                throw new InvalidOperationException("Host 构建修改了内置包文件集合。");
            foreach (var kv in before)
            {
                if (!after.TryGetValue(kv.Key, out string hash) || hash != kv.Value)
                    throw new InvalidOperationException($"Host 构建修改了内置包文件: {kv.Key}");
            }
        }

        private static void OfferStartServer(string version, string packageDirectory, string title)
        {
            var state = BuildStateStore.Load(out _);
            string playerPath = state.LastPlayer?.PlayerPath;
            string playerDirectory = string.IsNullOrEmpty(playerPath) ? "未记录" : Path.GetDirectoryName(playerPath);
            string batchDirectory = playerDirectory == "未记录" ? null : Directory.GetParent(playerDirectory)?.FullName;
            string symbolsDirectory = string.IsNullOrEmpty(batchDirectory) ? "未记录" : Path.Combine(batchDirectory, "Symbols");
            string successMessage =
                $"版本: {version}\n玩家目录: {playerDirectory}\n调试资料目录: {symbolsDirectory}\n资源目录: {packageDirectory}" +
                "\nPlayer 和内置包已验证。\n\n是否启动资源服？";
            if (!Application.isBatchMode && EditorUtility.DisplayDialog(title, successMessage, "启动资源服", "稍后"))
            {
                var result = LocalResourceServerUtility.StartAndProbe(packageDirectory, version);
                if (!result.Success)
                    ShowDialog("打包成功，资源服启动失败", result.Error);
                else
                    ShowDialog("资源服已启动", result.BaseUrl);
            }
            else
            {
                ShowDialog(title, successMessage);
            }
        }

        private static bool Confirm(string title, string message, string ok)
        {
            return Application.isBatchMode || EditorUtility.DisplayDialog(title, message, ok, "取消");
        }

        private static void ShowDialog(string title, string message)
        {
            if (Application.isBatchMode)
                Debug.Log($"[BuildUtility] 对话框消息：{title}：{message}");
            else
                EditorUtility.DisplayDialog(title, message, "OK");
        }

        private static void ShowResourceServerPublishResult(ResourceServerPublishResult result)
        {
            if (result == null)
            {
                Debug.LogError("[BuildUtility] 资源服发布结果为空。");
                ShowDialog("资源服操作失败", "资源服发布结果为空。");
                return;
            }

            if (!result.Success)
            {
                Debug.LogError($"[BuildUtility] {result.Operation}失败：{result.Error}");
                ShowDialog($"{result.Operation}失败", result.Error);
                return;
            }

            if (string.Equals(result.Operation, "更新资源服", StringComparison.Ordinal))
            {
                var unchanged = string.Equals(result.PreviousVersion, result.Version, StringComparison.Ordinal);
                var title = unchanged ? "资源版本未变化，已重新校验并替换" : "资源服更新成功";
                ShowDialog(
                    title,
                    $"旧版本: {result.PreviousVersion}\n新版本: {result.Version}\n发布包: {result.BundleDirectory}\n\n请停止服务器、整体替换 ResourceFiles、重新启动。");
                return;
            }

            ShowDialog(
                "资源服发布成功",
                $"版本: {result.Version}\n源资源: {result.SourcePackageDirectory}\n发布包: {result.BundleDirectory}\n\n请复制整个包到服务器设备并双击启动。");
        }

        private static string CreateVersion()
        {
            return PersistentBuildVersions.ReserveResource(BuildDataStore.Root, BuildTarget.StandaloneWindows64.ToString(),
                DateTime.Now, YooAssetBuildUtility.GetPackageRoot(BuildTarget.StandaloneWindows64));
        }

        private static string CreateVersionAfter(string previousVersion)
        {
            return PersistentBuildVersions.ReserveResource(BuildDataStore.Root, BuildTarget.StandaloneWindows64.ToString(),
                DateTime.Now, YooAssetBuildUtility.GetPackageRoot(BuildTarget.StandaloneWindows64), previousVersion);
        }

        private static string GetBuildStateRoot()
        {
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectPath, "Library", "OutpostLoopBuild");
        }

        private static string GetPreparatoryPlayerBuildPath()
        {
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectPath, "Temp", "OutpostLoopBuild", "PrepPlayer_Temp");
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"必要文件缺失: {path}");
        }

        private static void RequireDirectory(string path)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"必要目录缺失: {path}");
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    /// <summary>Windows 构建的只读安全规则；不切换平台，也不执行构建或发布。</summary>
    public static class WindowsBuildSafety
    {
        [Serializable]
        private sealed class AssemblyList
        {
            public string[] names;
            public int[] types;
        }

        public static bool TryValidateAssemblyList(
            string json,
            IEnumerable<string> hotUpdateAssemblies,
            out string error)
        {
            error = null;
            AssemblyList list;
            try
            {
                list = JsonUtility.FromJson<AssemblyList>(json);
            }
            catch (Exception exception)
            {
                error = $"程序集清单 JSON 无效：{exception.Message}";
                return false;
            }

            if (list?.names == null || list.types == null || list.names.Length != list.types.Length)
            {
                error = "程序集 names 与 types 缺失或长度不一致。";
                return false;
            }

            string[] required = hotUpdateAssemblies?.ToArray();
            if (required == null || required.Length == 0)
            {
                error = "热更程序集配置为空，无法验证 Player。";
                return false;
            }

            foreach (string name in required)
            {
                int index = Array.IndexOf(list.names, name);
                if (string.IsNullOrWhiteSpace(name) || index < 0 || list.types[index] != 16 ||
                    Array.LastIndexOf(list.names, name) != index)
                {
                    error = $"热更程序集 {name} 未唯一登记为用户 DLL（type=16）。";
                    return false;
                }
            }

            return true;
        }
    }
}
#endif
