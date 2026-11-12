using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>
    /// 定义存档分区采集与恢复边界，各玩法模块通过它交出和接收自己的持久化片段。
    /// </summary>
    public interface ISaveSectionProvider
    {
        string SectionId { get; }
        bool Validate(JToken data, out string error);
        JToken Capture();
        void Restore(JToken data);
        void Reset();
    }
}
