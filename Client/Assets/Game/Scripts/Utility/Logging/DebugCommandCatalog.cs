using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    /// <summary>内置开发指令的只读说明；参数和示例与解析共用同一目录，不执行任何业务。</summary>
    public sealed class DebugCommandDefinition
    {
        internal DebugCommandDefinition(string name, string usage, string description, int arguments = 0)
        { Name = name; Usage = usage; Description = description; ArgumentCount = arguments; }
        public string Name { get; }
        public string Usage { get; }
        public string Description { get; }
        public int ArgumentCount { get; }
    }

    /// <summary>已解析的单条命令；只包含目录身份和不可变参数，不携带运行对象。</summary>
    public sealed class DebugCommandRequest
    {
        internal DebugCommandRequest(DebugCommandDefinition definition, string[] arguments)
        { Definition = definition; Arguments = Array.AsReadOnly(arguments); }
        public DebugCommandDefinition Definition { get; }
        public IReadOnlyList<string> Arguments { get; }
    }

    /// <summary>统一维护指令目录与严格参数校验；仅接受单条带 # 前缀的白名单指令。</summary>
    public static class DebugCommandCatalog
    {
        public const int MaximumInputLength = 256;
        public static IReadOnlyList<DebugCommandDefinition> Entries { get; } = Array.AsReadOnly(new[] {
            new DebugCommandDefinition("help", "#help", "显示所有指令与用法"),
            new DebugCommandDefinition("version", "#version", "显示应用、Unity 和 PC 发布版本"),
            new DebugCommandDefinition("resource.status", "#resource.status", "查看当前资源来源、版本与更新检查状态"),
            new DebugCommandDefinition("save.status", "#save.status", "查看本地存档身份、待保存状态与最近错误"),
            new DebugCommandDefinition("save.flush", "#save.flush", "立即保存本地待提交修改"),
            new DebugCommandDefinition("log.clear", "#log.clear", "清空内存日志；保留错误文件")
        });

        /// <summary>在业务执行前拒绝空输入、未知指令、额外参数及多行粘贴。</summary>
        public static bool TryParse(string input, out DebugCommandRequest request, out string error)
        {
            request = null; error = null;
            if (string.IsNullOrWhiteSpace(input)) { error = "请输入指令，例如 #help。"; return false; }
            if (input.Length > MaximumInputLength) { error = "指令不能超过 256 个字符。"; return false; }
            if (input.IndexOfAny(new[] { '\r', '\n', ';' }) >= 0) { error = "每次只能执行一条指令。"; return false; }
            var parts = input.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (!parts[0].StartsWith("#", StringComparison.Ordinal)) { error = "指令必须以 # 开头，例如 #help。"; return false; }
            var definition = Entries.FirstOrDefault(entry => string.Equals(entry.Name, parts[0].Substring(1), StringComparison.OrdinalIgnoreCase));
            if (definition == null) { error = "未知指令，请执行 #help 查看目录。"; return false; }
            if (parts.Length != definition.ArgumentCount + 1) { error = "用法：" + definition.Usage; return false; }
            request = new DebugCommandRequest(definition, parts.Skip(1).ToArray());
            return true;
        }

        public static string HelpText => string.Join("\n", Entries.Select(entry => entry.Usage + " — " + entry.Description));
    }
}
