namespace Game
{
    /// <summary>本局存档检查结果；损坏或不支持的文件不能被新局覆盖。</summary>
    public enum SaveFileStatus { Missing, Valid, Corrupted, Unsupported }
    /// <summary>街机文件读取快照与诊断信息，不持有任何场景对象。</summary>
    public sealed class SaveReadResult<T>
    {
        public SaveFileStatus Status { get; set; }
        public T Data { get; set; }
        public string Error { get; set; }
        public bool FromBackup { get; set; }
        public bool IsValid => Status == SaveFileStatus.Valid && Data != null;
    }
}
