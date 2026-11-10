using OutpostLoop.Config;

namespace OutpostLoop
{
    /// <summary>本局的单一资金账户、基地耐久、升级与结算。</summary>
    public sealed class RunState
    {
        public PrototypeConfig Config { get; }
        public int Funds { get; private set; }
        public int Capacity => 80 + Levels[0] * 20;
        public float Elapsed => (float)elapsed;
        // 分钟级任务使用双精度累计，避免固定步长累计误差影响达标时刻。
        private double elapsed;
        public bool Paused { get; set; }
        public int Health { get; private set; }
        public int Outcome { get; private set; }
        public int[] Levels { get; } = new int[3];
        // 同一种子重开仍是另一局，胜场独立结算。
        internal string RewardId { get; } = System.Guid.NewGuid().ToString("N");

        public RunState(PrototypeConfig config = null, int startingFunds = 0)
        {
            Config = config ?? PrototypeServices.LoadConfig();
            Funds = startingFunds;
        }

        /// <summary>主基地确认落位才具有耐久。</summary>
        internal void EstablishBase() => Health = Config.InitialHealth;

        /// <summary>探索时生命属于指挥员，结束后结清未带出的物资。</summary>
        internal void DeployCommander(int health) => Health = health;
        internal void FinishExpedition(bool returned)
        {
            Outcome = returned ? 1 : -1;
            Funds = 0;
        }

        public int UpgradeCost(int route) => route >= 0 && route < Levels.Length ? 200 * (Levels[route] + 1) : int.MaxValue;

        /// <summary>扣除统一资金，耐久路线在升级时修复主基地。</summary>
        public bool Upgrade(int route)
        {
            if (route < 0 || route >= Levels.Length || Paused || Outcome != 0 || Health <= 0 ||
                Levels[route] >= Config.MaxLevel || !TrySpend(UpgradeCost(route))) return false;
            Levels[route]++;
            if (route == 2) Health = System.Math.Min(Config.InitialHealth + Levels[2] * Config.HealthPerLevel, Health + Config.UpgradeRepair);
            return true;
        }

        internal bool CanAfford(int cost) => cost >= 0 && Funds >= cost;

        internal bool TrySpend(int cost)
        {
            if (Paused || Outcome != 0 || !CanAfford(cost)) return false;
            Funds -= cost;
            return true;
        }

        /// <summary>只有采集员实际卸货才增加资金，开局档位上限不限制收入。</summary>
        internal void CreditDelivery(int amount) => Funds += amount;

        /// <summary>局时只用于调度敌人和进度，最后一波已全部入场并被消灭才获胜。</summary>
        internal void Tick(float seconds, bool clearedFinalWave)
        {
            if (Paused || Outcome != 0) return;
            elapsed += seconds;
            if (clearedFinalWave) Outcome = 1;
        }

        public void Damage(int amount)
        {
            if (Paused || Outcome != 0 || Health <= 0 || amount <= 0) return;
            Health = System.Math.Max(0, Health - amount);
            if (Health == 0) Outcome = -1;
        }
    }
}
