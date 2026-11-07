using QFramework;
namespace Game
{
    /// <summary>独立技术容器。玩法模块由启动入口注册，不引用旧街机业务。</summary>
    public sealed class Game : Architecture<Game>
    {
        protected override void Init() { }
    }
}
