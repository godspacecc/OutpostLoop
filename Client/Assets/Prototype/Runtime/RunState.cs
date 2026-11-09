namespace OutpostLoop
{
    /// <summary>独立可玩预览的纯规则状态；不引用旧项目资源或玩家数据。</summary>
    public sealed class RunState
    {
        /// <summary>本局只读配置，由权威表生成并在创建时固定。</summary>
        public OutpostLoop.Config.PrototypeConfig Config { get; }
        public RunState(OutpostLoop.Config.PrototypeConfig config=null)
        { Config=config??PrototypeServices.LoadConfig();Health=Config.InitialHealth; }
        // 状态只通过本类的事务方法修改；表现层不直接写资源。
        public int Scrap { get; private set; }
        public int Energy { get; private set; }
        public int CargoScrap { get; private set; }
        public int CargoEnergy { get; private set; }
        public int Capacity => Config.CapacityBase + Levels[0] * Config.CapacityPerLevel;
        public float Elapsed { get; private set; }
        public bool Paused { get; set; }
        public int Health { get; private set; }
        public int Outcome { get; private set; }
        public int[] Levels { get; } = new int[3];
        // 每次新局使用独立结算标识，地图种子重复时仍可正常获得奖励。
        internal string RewardId { get; } = System.Guid.NewGuid().ToString("N");

        /// <summary>开局补给直接入库，由会话在支付成功后发放一次。</summary>
        internal void AddStarterSupplies(bool scrap, bool energy)
        {
            if (scrap) Scrap += PrototypeWallet.ScrapPack;
            if (energy) Energy += PrototypeWallet.EnergyPack;
        }
        /// <summary>采集站的自动补给直接进入基地库存，不占用工人背包。</summary>
        internal void ReceiveExtractedResource(bool energy)
        {
            if (energy) Energy++;
            else Scrap++;
        }
        /// <summary>尝试装入一单位资源；暂停、终局与满载时拒绝。</summary>
        public bool Gather(bool energy)
        {
            if (Paused || Outcome != 0 || CargoScrap + CargoEnergy >= Capacity) return false;
            if (energy) CargoEnergy++; else CargoScrap++;
            return true;
        }
        /// <summary>回到基地后一次性入库，重复调用不会重复入账。</summary>
        public void Deposit()
        {
            if (Paused || Outcome != 0) return;
            Scrap += CargoScrap; Energy += CargoEnergy;
            CargoScrap = CargoEnergy = 0;
        }
        /// <summary>查询下一等级价格；首级只消耗废料，之后需要能源。</summary>
        public int ScrapCost(int route) => route >= 0 && route < 3 ? Config.ScrapCostBase + Levels[route] * Config.ScrapCostPerLevel : int.MaxValue;
        public int EnergyCost(int route) => route >= 0 && route < 3 ? Levels[route] * Config.EnergyCostPerLevel : int.MaxValue;
        /// <summary>校验全部条件后原子扣费并升级；耐久路线同时修复基地。</summary>
        public bool Upgrade(int route)
        {
            if (route < 0 || route >= 3 || Paused || Outcome != 0 || Levels[route] >= Config.MaxLevel) return false;
            int scrap = ScrapCost(route), energy = EnergyCost(route);
            if (!TrySpend(scrap, energy)) return false;
            Levels[route]++;
            if (route == 2) Health = System.Math.Min(Config.InitialHealth + Levels[2] * Config.HealthPerLevel, Health + Config.UpgradeRepair);
            return true;
        }
        /// <summary>原子扣除基地库存，供基地面板在暂停期间建造。</summary>
        internal bool TrySpend(int scrap, int energy)
        {
            if (Outcome != 0 || scrap < 0 || energy < 0 || Scrap < scrap || Energy < energy) return false;
            Scrap -= scrap;
            Energy -= energy;
            return true;
        }
        /// <summary>推进有界局时；到期不无限拖延，仍有敌人时判防守失败。</summary>
        public void Tick(float seconds, int enemies)
        {
            if (Paused || Outcome != 0 || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            Elapsed = System.Math.Min(Config.Duration, Elapsed + seconds);
            if (Elapsed >= Config.Duration) Outcome = Health > 0 && enemies == 0 ? 1 : -1;
        }
        /// <summary>扣除非负基地伤害，死亡后只结算一次。</summary>
        public void Damage(int amount)
        {
            if (Paused || Outcome != 0 || amount <= 0) return;
            Health = System.Math.Max(0, Health - amount);
            if (Health == 0) Outcome = -1;
        }
    }
}
