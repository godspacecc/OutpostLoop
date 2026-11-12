namespace Game
{
    /// <summary>
    /// 表示红点节点的不可变显示状态，包含是否点亮和聚合计数。
    /// </summary>
    public readonly struct RedPointState
    {
        public RedPointState(string id, bool active, int count)
        {
            Id = id ?? string.Empty;
            Active = active;
            Count = count < 0 ? 0 : count;
        }

        public string Id { get; }
        public bool Active { get; }
        public int Count { get; }
    }

    /// <summary>
    /// 红点状态变化通知，携带变更后的不可变节点状态。
    /// </summary>
    public readonly struct RedPointChangedEvent
    {
        public RedPointChangedEvent(string id, bool active, int count)
        {
            Id = id ?? string.Empty;
            Active = active;
            Count = count < 0 ? 0 : count;
        }

        public string Id { get; }
        public bool Active { get; }
        public int Count { get; }
    }
}
