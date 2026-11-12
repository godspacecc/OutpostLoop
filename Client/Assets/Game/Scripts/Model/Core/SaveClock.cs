using System;
namespace Game
{
    /// <summary>街机文件事务的时间来源，可由定向测试注入。</summary>
    public interface ISaveClock
    {
        DateTime UtcNow { get; }
        float RealtimeSinceStartup { get; }
    }
    /// <summary>正式街机使用 UTC 时间标记安全节点。</summary>
    public sealed class UnitySaveClock : ISaveClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public float RealtimeSinceStartup => UnityEngine.Time.realtimeSinceStartup;
    }
}
