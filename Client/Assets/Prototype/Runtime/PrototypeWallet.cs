using System;
using System.IO;
using Game;
using QFramework;

namespace OutpostLoop
{
    /// <summary>跨局保存胜场和上次结算标识，旧版补给币随存档保留。</summary>
    public sealed class PrototypeWallet : IUtility
    {
        public const int InitialFunds = 800;
        private const string FileName = "prototype-wallet.json";

        [Serializable]
        private sealed class BalanceData
        {
            public int coins;
            public int wins;
            public string lastVictory;
        }

        private readonly JsonFileStore<BalanceData> store;
        private BalanceData data;
        public int Wins => data?.wins ?? 0;
        public int StartingFunds => Wins >= 3 ? 1000 : Wins >= 1 ? 900 : InitialFunds;

        public PrototypeWallet(string directory = null)
        {
            store = new JsonFileStore<BalanceData>(directory);
            var read = store.Read(FileName);
            if (read.Status == SaveFileStatus.Missing) data = new BalanceData();
            else if (read.Status == SaveFileStatus.Valid && read.Data.coins >= 0 && read.Data.wins >= 0) data = read.Data;
        }

        /// <summary>先保存再公布新档位，重试同一局不会重复计入胜场。</summary>
        public bool RecordVictory(string runId)
        {
            if (data == null || string.IsNullOrEmpty(runId)) return false;
            if (data.lastVictory == runId) return true;
            var next = new BalanceData
            {
                coins = data.coins,
                wins = data.wins < int.MaxValue ? data.wins + 1 : data.wins,
                lastVictory = runId
            };
            try
            {
                store.Save(FileName, next);
                data = next;
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Log.Warning("胜场保存失败：" + error.GetType().Name);
                return false;
            }
        }
    }
}
