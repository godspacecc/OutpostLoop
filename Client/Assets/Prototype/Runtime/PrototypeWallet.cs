using System;
using System.IO;
using Game;
using QFramework;

namespace OutpostLoop
{
    /// <summary>原型跨局补给币，只保存余额和最近一次结算标识。</summary>
    public sealed class PrototypeWallet : IUtility
    {
        public const int VictoryCoins = 10;
        public const int SupplyPrice = 5;
        public const int ScrapPack = 8;
        public const int EnergyPack = 3;
        private const string FileName = "prototype-wallet.json";

        [Serializable]
        private sealed class BalanceData
        {
            public int coins;
            public string lastVictory;
        }

        private readonly JsonFileStore<BalanceData> store;
        private BalanceData data;
        public int Coins => data?.coins ?? 0;

        public PrototypeWallet(string directory = null)
        {
            store = new JsonFileStore<BalanceData>(directory);
            var read = store.Read(FileName);
            if (read.Status == SaveFileStatus.Missing) data = new BalanceData();
            else if (read.Status == SaveFileStatus.Valid && read.Data.coins >= 0) data = read.Data;
        }

        /// <summary>先落盘再公布余额，同一局的重复结算不会重复发放。</summary>
        internal bool RewardVictory(string runId)
        {
            if (data == null || string.IsNullOrEmpty(runId)) return false;
            if (data.lastVictory == runId) return true;
            int coins = (int)Math.Min(int.MaxValue, (long)data.coins + VictoryCoins);
            return Save(new BalanceData { coins = coins, lastVictory = runId });
        }

        /// <summary>支付失败时保留旧余额，调用方也不会发放物资。</summary>
        internal bool Pay(int price)
        {
            if (data == null || price <= 0 || data.coins < price) return false;
            return Save(new BalanceData { coins = data.coins - price, lastVictory = data.lastVictory });
        }

        private bool Save(BalanceData next)
        {
            try
            {
                store.Save(FileName, next);
                data = next;
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Log.Warning("补给币保存失败：" + error.GetType().Name);
                return false;
            }
        }
    }
}
