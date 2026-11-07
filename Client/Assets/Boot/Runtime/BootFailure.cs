using System;
using System.Collections.Generic;
using System.Text;

namespace Boot
{
    public enum BootFailureStage
    {
        BootstrapConfig,
        ResourceInitialization,
        ResourceVersion,
        ResourceManifest,
        ResourceDownload,
        ResourceValidation,
        AotList,
        AotMetadata,
        GameAssembly,
        MainScene,
        MainInitialization,
        Unexpected
    }

    public enum BootFailureCode
    {
        BootstrapConfigInvalid,
        HostInitializationFailed,
        PackageVersionRequestFailed,
        PackageManifestUpdateFailed,
        ResourceDownloadFailed,
        BuiltinResourceInvalid,
        RequiredResourceMissing,
        AotListInvalid,
        AotDllMissing,
        AotMetadataRejected,
        AotMetadataException,
        GameDllMissing,
        GameDllLoadFailed,
        MainSceneLoadFailed,
        MainInitializationFailed,
        UnexpectedException,
        HostIncompatible
    }

    [Flags]
    public enum BootFailureAction
    {
        None = 0,
        Retry = 1,
        CopyDetails = 2,
        Quit = 4
    }

    public enum BootRetryScope
    {
        None,
        ResourceInitialization,
        MainScene
    }

    public sealed class BootFailure
    {
        private const int MaxDiagnosticLength = 2048;
        private const string PackageGuidance = "请使用正确的完整安装包后重新启动游戏。";
        private const string RetryGuidance = "请检查网络连接或稍后重新尝试；如果问题持续，请复制错误信息后联系支持。";

        private BootFailure(
            BootFailureCode code,
            BootFailureStage stage,
            BootFailureAction actions,
            BootRetryScope retryScope,
            string playerTitle,
            string playerGuidance,
            string diagnosticDetails)
        {
            bool hasRetry = (actions & BootFailureAction.Retry) != 0;
            if (hasRetry != (retryScope != BootRetryScope.None))
            {
                throw new ArgumentException("Retry action and retry scope must be consistent.");
            }

            Code = code;
            Stage = stage;
            Actions = actions;
            RetryScope = retryScope;
            PlayerTitle = playerTitle ?? throw new ArgumentNullException(nameof(playerTitle));
            PlayerGuidance = playerGuidance ?? throw new ArgumentNullException(nameof(playerGuidance));
            DiagnosticDetails = diagnosticDetails ?? throw new ArgumentNullException(nameof(diagnosticDetails));
        }

        public BootFailureCode Code { get; }
        public BootFailureStage Stage { get; }
        public BootFailureAction Actions { get; }
        public BootRetryScope RetryScope { get; }
        public string PlayerTitle { get; }
        public string PlayerGuidance { get; }
        public string DiagnosticDetails { get; }

        public static BootFailure Create(
            BootFailureCode code,
            BootFailureStage stage,
            BootRetryScope retryScope = BootRetryScope.None,
            IReadOnlyDictionary<string, string> diagnosticFields = null)
        {
            BootFailureAction actions = GetActions(code);
            if ((actions & BootFailureAction.Retry) != 0)
            {
                retryScope = retryScope == BootRetryScope.None ? GetDefaultRetryScope(code) : retryScope;
            }
            else
            {
                retryScope = BootRetryScope.None;
            }

            string title = GetTitle(code);
            string guidance = GetGuidance(code);
            string diagnosticDetails = BuildDiagnostic(code, stage, retryScope, guidance, diagnosticFields);
            return new BootFailure(code, stage, actions, retryScope, title, guidance, diagnosticDetails);
        }

        public static BootFailure Unexpected(Exception exception, BootFailureStage stage)
        {
            var fields = new Dictionary<string, string>
            {
                ["exceptionType"] = exception?.GetType().FullName,
                ["hResult"] = exception?.HResult.ToString()
            };
            return Create(BootFailureCode.UnexpectedException, stage, BootRetryScope.None, fields);
        }

        public bool Allows(BootFailureAction action)
        {
            return (Actions & action) != 0;
        }

        private static BootFailureAction GetActions(BootFailureCode code)
        {
            switch (code)
            {
                case BootFailureCode.HostInitializationFailed:
                case BootFailureCode.PackageVersionRequestFailed:
                case BootFailureCode.PackageManifestUpdateFailed:
                case BootFailureCode.ResourceDownloadFailed:
                case BootFailureCode.MainSceneLoadFailed:
                    return BootFailureAction.Retry | BootFailureAction.CopyDetails | BootFailureAction.Quit;
                default:
                    return BootFailureAction.CopyDetails | BootFailureAction.Quit;
            }
        }

        private static BootRetryScope GetDefaultRetryScope(BootFailureCode code)
        {
            return code == BootFailureCode.MainSceneLoadFailed
                ? BootRetryScope.MainScene
                : BootRetryScope.ResourceInitialization;
        }

        private static string GetTitle(BootFailureCode code)
        {
            switch (code)
            {
                case BootFailureCode.BootstrapConfigInvalid:
                    return "启动配置无效";
                case BootFailureCode.HostInitializationFailed:
                    return "资源服务器连接失败";
                case BootFailureCode.PackageVersionRequestFailed:
                    return "资源版本检测失败";
                case BootFailureCode.PackageManifestUpdateFailed:
                    return "资源清单更新失败";
                case BootFailureCode.ResourceDownloadFailed:
                    return "资源下载失败";
                case BootFailureCode.BuiltinResourceInvalid:
                    return "内置资源无效";
                case BootFailureCode.RequiredResourceMissing:
                    return "必需资源缺失";
                case BootFailureCode.HostIncompatible:
                    return "需要更新完整安装包";
                case BootFailureCode.AotListInvalid:
                    return "AOT 清单无效";
                case BootFailureCode.AotDllMissing:
                    return "AOT 文件缺失";
                case BootFailureCode.AotMetadataRejected:
                    return "AOT 元数据不兼容";
                case BootFailureCode.AotMetadataException:
                    return "AOT 元数据加载异常";
                case BootFailureCode.GameDllMissing:
                    return "Game.dll 缺失";
                case BootFailureCode.GameDllLoadFailed:
                    return "Game.dll 加载失败";
                case BootFailureCode.MainSceneLoadFailed:
                    return "主场景加载失败";
                case BootFailureCode.MainInitializationFailed:
                    return "游戏初始化失败";
                default:
                    return "启动流程异常";
            }
        }

        private static string GetGuidance(BootFailureCode code)
        {
            switch (code)
            {
                case BootFailureCode.HostInitializationFailed:
                case BootFailureCode.PackageVersionRequestFailed:
                case BootFailureCode.PackageManifestUpdateFailed:
                case BootFailureCode.ResourceDownloadFailed:
                    return RetryGuidance;
                case BootFailureCode.MainSceneLoadFailed:
                    return "主场景暂时无法进入，请重新尝试；如果问题持续，请复制错误信息后联系支持。";
                case BootFailureCode.HostIncompatible:
                case BootFailureCode.AotListInvalid:
                case BootFailureCode.AotDllMissing:
                case BootFailureCode.AotMetadataRejected:
                case BootFailureCode.AotMetadataException:
                case BootFailureCode.GameDllMissing:
                case BootFailureCode.GameDllLoadFailed:
                    return PackageGuidance;
                case BootFailureCode.MainInitializationFailed:
                    return "游戏初始化未能完成，请复制错误信息后联系支持，或退出后重新启动。";
                default:
                    return "启动流程无法继续，请复制错误信息后联系支持，或退出后重新启动。";
            }
        }

        private static string BuildDiagnostic(
            BootFailureCode code,
            BootFailureStage stage,
            BootRetryScope retryScope,
            string guidance,
            IReadOnlyDictionary<string, string> fields)
        {
            var builder = new StringBuilder();
            AppendLine(builder, "Code", code.ToString());
            AppendLine(builder, "Stage", stage.ToString());
            AppendLine(builder, "RetryScope", retryScope.ToString());

            if (fields != null)
            {
                foreach (var pair in fields)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrEmpty(pair.Value))
                    {
                        continue;
                    }

                    AppendLine(builder, SanitizeKey(pair.Key), SanitizeValue(pair.Value));
                }
            }

            AppendLine(builder, "Guidance", guidance);

            string text = builder.ToString();
            if (text.Length <= MaxDiagnosticLength)
            {
                return text;
            }

            string required = $"Code: {code}\nStage: {stage}\nRetryScope: {retryScope}\nGuidance: {guidance}\n";
            int remaining = Math.Max(0, MaxDiagnosticLength - required.Length);
            string optional = text.Substring(0, Math.Min(remaining, text.Length));
            return optional + required;
        }

        private static void AppendLine(StringBuilder builder, string key, string value)
        {
            builder.Append(key);
            builder.Append(": ");
            builder.Append(value);
            builder.Append('\n');
        }

        private static string SanitizeKey(string key)
        {
            var builder = new StringBuilder(key.Length);
            foreach (char c in key)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.ToString();
        }

        private static string SanitizeValue(string value)
        {
            string text = value.Replace('\\', '/').Replace("\r", " ").Replace("\n", " ");
            int lastSlash = text.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash + 1 < text.Length)
            {
                text = text.Substring(lastSlash + 1);
            }

            const int MaxFieldLength = 160;
            return text.Length <= MaxFieldLength ? text : text.Substring(0, MaxFieldLength);
        }
    }

    public sealed class BootOperationResult
    {
        private BootOperationResult(bool succeeded, BootFailure failure)
        {
            if (succeeded && failure != null)
            {
                throw new ArgumentException("Success result cannot carry a failure.");
            }

            if (!succeeded && failure == null)
            {
                throw new ArgumentNullException(nameof(failure));
            }

            Succeeded = succeeded;
            Failure = failure;
        }

        public bool Succeeded { get; }
        public BootFailure Failure { get; }

        public static BootOperationResult Success()
        {
            return new BootOperationResult(true, null);
        }

        public static BootOperationResult Failed(BootFailure failure)
        {
            if (failure == null)
            {
                throw new ArgumentNullException(nameof(failure));
            }

            return new BootOperationResult(false, failure);
        }
    }
}
