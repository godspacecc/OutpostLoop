using System;
using Boot;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>串行执行白名单调试指令；只通过现有系统操作本地基础服务，不执行任意代码或服务端管理操作。</summary>
    public sealed class DebugCommandSystem : AbstractSystem
    {
        // 本项目的白名单指令只查询或同步提交本地数据。
        protected override void OnInit() { }

        /// <summary>执行单条白名单指令，将结果写入运行时日志。</summary>
        public UniTask ExecuteAsync(string input)
        {
            if (!DebugCommandCatalog.TryParse(input, out var request, out var error))
            {
                Report(false, error);
                return UniTask.CompletedTask;
            }
            try
            {
                var result = Execute(request);
                Report(result.success, "#" + request.Definition.Name + "\n" + result.message);
            }
            catch (Exception exception)
            {
                Report(false, "指令执行异常：" + exception);
            }
            return UniTask.CompletedTask;
        }

        /// <summary>调用基础模块的既有接口，不持有玩法或服务器状态。</summary>
        private (bool success, string message) Execute(DebugCommandRequest request)
        {
            switch (request.Definition.Name)
            {
                case "help": return (true, DebugCommandCatalog.HelpText);
                case "version":
                    var release = WindowsUpdateManifest.ReadInstalled();
                    return (true, $"应用：{Application.version}\nUnity：{Application.unityVersion}\n平台：{Application.platform}\nPC 发布序号：{release?.releaseNumber.ToString() ?? "未内置"}");
                case "resource.status":
                    return BootHost.TryGetResourceInfo(out var resource)
                        ? (true, $"来源：{resource.Source}\n当前版本：{resource.ActiveVersion}\n远端版本：{resource.HostVersion}")
                        : (true, "当前没有 Boot 资源启动快照。");
                case "save.status":
                    var saves = this.GetSystem<SaveSystem>();
                    var save = this.GetModel<SaveModel>();
                    return (true, $"本地存档：{save.CurrentSaveId ?? "无"}\n名称：{save.CurrentData?.SaveName ?? "无"}\n待保存：{saves.HasPendingChanges}\n最近保存 UTC：{saves.LastSavedAtUtc?.ToString("O") ?? "无"}\n最近错误：{saves.LastSaveError ?? "无"}");
                case "save.flush":
                    if (this.GetModel<SaveModel>().CurrentData == null) return (false, "当前没有活动的本地存档。");
                    bool saved = this.GetSystem<SaveSystem>().Flush(out var error);
                    return (saved, saved ? "本地修改已保存；没有待保存修改时不重复写盘。" : error);
                case "log.clear": this.GetSystem<RuntimeLogSystem>().Clear(); return (true, "内存日志已清空，错误文件保留。");
                default: return (false, "指令目录与执行入口不一致。");
            }
        }

        private void Report(bool success, string message)
        {
            this.GetModel<RuntimeLogModel>().Add(new RuntimeLogEntry(DateTime.Now,
                success ? LogType.Log : LogType.Warning, "[调试指令] " + message, string.Empty));
        }
    }
}
