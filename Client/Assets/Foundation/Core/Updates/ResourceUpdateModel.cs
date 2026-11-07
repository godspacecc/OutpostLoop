using QFramework;

namespace Game
{
    /// <summary>
    /// 保存资源快照和各更新分支最近一次有效结论。
    /// </summary>
    public sealed class ResourceUpdateModel : AbstractModel
    {
        public BindableProperty<ResourceUpdateSnapshot> Snapshot { get; } =
            new BindableProperty<ResourceUpdateSnapshot>(ResourceUpdateSnapshot.NotChecked);

        public bool ResourceUpdateAvailable { get; internal set; }
        public bool ApplicationUpdateAvailable { get; internal set; }
        public bool HasUpdate { get; internal set; }

        protected override void OnInit()
        {
        }
    }
}
