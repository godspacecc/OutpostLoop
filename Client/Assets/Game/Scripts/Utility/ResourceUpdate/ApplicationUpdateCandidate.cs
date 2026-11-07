namespace Game
{
    /// <summary>Game 展示安装包更新所需的数据；不包含安装器、文件路径或平台执行逻辑。</summary>
    public sealed class ApplicationUpdateCandidate
    {
        public string versionName;
        public long sizeBytes;
    }
}
