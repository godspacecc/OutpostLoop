namespace Boot
{
    /// <summary>
    /// 资源运行模式
    /// </summary>
    public enum ResourcePlayMode
    {
        /// <summary>
        /// 编辑器模拟模式 - 仅在 Unity 编辑器中可用
        /// </summary>
        EditorSimulateMode,

        /// <summary>
        /// 离线模式 - 使用内置的 StreamingAssets
        /// </summary>
        OfflinePlayMode,

        /// <summary>
        /// 主机模式 - 从远程服务器下载资源
        /// </summary>
        HostPlayMode
    }
}