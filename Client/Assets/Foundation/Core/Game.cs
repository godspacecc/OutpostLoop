using QFramework;
namespace Game
{
    /// <summary>独立技术容器。玩法模块由启动入口注册，不引用旧街机业务。</summary>
    public sealed class Game : Architecture<Game>
    {
        /// <summary>先注册日志模型再启动采集，领域模块随后由各入口接入。</summary>
        protected override void Init()
        {
            RegisterModel(new RuntimeLogModel());
            RegisterSystem(new RuntimeLogSystem());
        }
    }
}
