using QFramework;

namespace Game
{
    /// <summary>
    /// 保存当前已进入的存档数据；尚未进入存档时 CurrentData 允许为空，事务代码通过 Clone 隔离回滚快照。
    /// </summary>
    public sealed class SaveModel : AbstractModel
    {
        public string CurrentSaveId { get; private set; }
        public SaveGameData CurrentData { get; private set; }

        protected override void OnInit()
        {
        }

        public void SetCurrent(SaveGameData data)
        {
            CurrentData = data?.Clone();
            CurrentSaveId = CurrentData?.SaveId;
        }

        public void ClearCurrent()
        {
            CurrentData = null;
            CurrentSaveId = null;
        }
    }
}
