using System;
using UnityEngine;

namespace Boot
{
    [Serializable]
    public sealed class ApkInstallResult
    {
        public int schemaVersion;
        public int result = -1;
        public string errorCode;
        public string stage;
        public string details;

        public static ApkInstallResult Failure(string code, string stage, string details) =>
            new ApkInstallResult { schemaVersion = 1, result = -1, errorCode = code, stage = stage, details = details };

        public static ApkInstallResult Parse(string json)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(json) && json.Length <= 65536)
                {
                    var value = JsonUtility.FromJson<ApkInstallResult>(json);
                    if (value != null && value.schemaVersion == 1 && !string.IsNullOrWhiteSpace(value.stage) &&
                        value.result >= -1 && value.result <= 2) return value;
                }
            }
            catch (ArgumentException) { }
            return Failure("INVALID_BRIDGE_RESULT", "bridge", "安装插件未返回有效结果。");
        }

        public string PlayerMessage => errorCode switch
        {
            "SIGNATURE_MISMATCH" => "安装包签名与当前应用不一致，请联系开发者重新发布。",
            "PACKAGE_MISMATCH" => "安装包不属于当前应用，请联系开发者确认包名。",
            "VERSION_MISMATCH" => "安装包版本与发布信息不一致，请重新下载。",
            "APK_UNREADABLE" => "系统无法解析安装包，请重新下载。",
            "INVALID_FILE" => "安装包文件不存在或路径无效，请重新下载。",
            "INSTALL_TIMEOUT" => "打开系统安装界面超时，可以重试或稍后更新。",
            "ACTIVITY_NOT_FOUND" => "系统没有可用的安装或授权界面。",
            "SECURITY_EXCEPTION" => "系统拒绝打开安装界面，请检查安装权限。",
            _ => "无法打开安装更新，可以稍后重试。"
        };

        public string ToCopyDetails(ApkUpdateManifest manifest)
        {
            string text = $"安装更新失败\n阶段：{stage}\n错误码：{errorCode}\n结果：{result}\n" +
                $"包名：{Application.identifier}\n当前版本：{Application.version}\n目标版本：{manifest.versionName}\n" +
                $"目标版本码：{manifest.versionCode}\n文件：{manifest.apkFileName}\n大小：{manifest.sizeBytes} bytes\n" +
                $"SHA-256：{manifest.sha256}\n{details}";
            return text.Length <= 16384 ? text : text.Substring(0, 16300) + "\n[详情过长，已截断]";
        }
    }
}
