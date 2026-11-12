using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// 表示红点树节点定义，父子关系由 ID 连接，运行期状态由 RedPointSystem 聚合。
    /// </summary>
    public sealed class RedPointNode
    {
        public RedPointNode(string id, string parentId)
        {
            Id = id;
            ParentId = parentId;
        }

        public string Id { get; }
        public string ParentId { get; }
        public List<string> Children { get; } = new List<string>();
        public bool LeafActive { get; set; }
        public int LeafCount { get; set; }
        public RedPointState State { get; set; }
        public bool Dirty { get; set; } = true;
    }

}
