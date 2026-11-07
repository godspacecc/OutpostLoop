using System;

namespace Game
{
    /// <summary>校验既有 UI 完整逻辑地址；不接受磁盘路径、扩展名或路径跳转。</summary>
    public static class UIResourceAddress
    {
        public static bool IsValid(string address)
        {
            const string root = "Prefabs/UI/";
            if (string.IsNullOrWhiteSpace(address) || !address.StartsWith(root, StringComparison.Ordinal)
                || address.IndexOfAny(new[] { '\\', ':', '?', '#', '*' }) >= 0)
                return false;

            foreach (var segment in address.Substring(root.Length).Split('/'))
            {
                if (string.IsNullOrWhiteSpace(segment) || segment != segment.Trim()
                    || segment == "." || segment == ".." || segment.IndexOf('.') >= 0)
                    return false;
            }
            return true;
        }
    }
}
