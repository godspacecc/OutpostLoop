#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Boot;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>正式 PC Player 的发布身份事务；默认从外部台账预留序号，失败恢复源码身份但不退还预留。</summary>
    public sealed class WindowsReleaseIdentity : IDisposable
    {
        private const string DefaultLedger = "Assets/Editor/Build/windows-release-version.json";
        private const string DefaultEmbedded = "Assets/StreamingAssets/windows-release.json";
        private readonly string Ledger;
        private readonly string Embedded;
        private readonly Action<string> synchronize;
        private readonly byte[] previousEmbedded;
        private readonly byte[] previousLedger;
        private readonly WindowsUpdateManifest identity;
        private bool committed;

        public WindowsReleaseIdentity() : this(DefaultLedger, DefaultEmbedded,
            path => { if (File.Exists(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                      else AssetDatabase.DeleteAsset(path); }, ReserveDefault()) { }

        /// <summary>昂贵构建前校验本项目版本起点；只读，不预留版本、不写入台账。</summary>
        public static WindowsUpdateManifest ValidateSourceIdentity()
        {
            var seed = JsonUtility.FromJson<WindowsUpdateManifest>(File.ReadAllText(DefaultLedger).TrimStart('\uFEFF'));
            if (seed == null || seed.schemaVersion != 1 || seed.productId != WindowsUpdateManifest.ProductId ||
                seed.platform != "Windows" || seed.architecture != "x64" || seed.releaseNumber < 0 ||
                !System.Text.RegularExpressions.Regex.IsMatch(seed.versionName ?? "", @"\A[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}\z"))
                throw new InvalidOperationException("PC 源码版本起点无效。");
            return seed;
        }

        private static ApplicationVersionReservation ReserveDefault()
        {
            var seed = ValidateSourceIdentity();
            return PersistentBuildVersions.ReserveApplication(BuildDataStore.Root, "StandaloneWindows64", seed.versionName, seed.releaseNumber);
        }

        /// <summary>隔离测试的旧式身份事务，不访问默认持久目录；生产入口使用无参构造。</summary>
        public WindowsReleaseIdentity(string ledger, string embedded, Action<string> synchronize)
            : this(ledger, embedded, synchronize, null) { }

        public WindowsReleaseIdentity(string ledger, string embedded, Action<string> synchronize, ApplicationVersionReservation reservation)
        {
            Ledger = ledger; Embedded = embedded;
            this.synchronize = synchronize ?? (_ => { });
            previousLedger = File.ReadAllBytes(Ledger);
            var previous = JsonUtility.FromJson<WindowsUpdateManifest>(System.Text.Encoding.UTF8.GetString(previousLedger).TrimStart('\uFEFF'));
            if (previous == null || previous.schemaVersion != 1 || previous.platform != "Windows" ||
                previous.architecture != "x64" || previous.productId != WindowsUpdateManifest.ProductId ||
                previous.releaseNumber < 0 || previous.releaseNumber == int.MaxValue)
                throw new InvalidOperationException("PC 发布版本台账无效。");
            identity = new WindowsUpdateManifest {
                schemaVersion = 1, productId = WindowsUpdateManifest.ProductId, platform = "Windows",
                architecture = "x64", versionName = reservation?.VersionName ?? previous.versionName,
                releaseNumber = reservation?.VersionCode ?? previous.releaseNumber + 1
            };
            if (!identity.ValidIdentity) throw new InvalidOperationException("PC 版本名必须为三段数字。");
            previousEmbedded = File.Exists(Embedded) ? File.ReadAllBytes(Embedded) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(Embedded));
            try
            {
                WriteAtomically(Embedded, Encoding.UTF8.GetBytes(JsonUtility.ToJson(identity, true)));
                this.synchronize(Embedded);
            }
            catch (Exception original)
            {
                // 回滚失败必须同时保留最初错误，不能让恢复异常掩盖构建失败的位置。
                try { Restore(); }
                catch (Exception rollback)
                { throw new AggregateException("PC 发布身份准备失败，且恢复失败。", original, rollback); }
                throw;
            }
        }

        /// <summary>提交前核对实际 Player 输出；源 StreamingAssets 正确不代表构建产物已携带身份。</summary>
        public void ValidatePlayer(string playerDirectory)
        {
            string path = Path.Combine(playerDirectory, "OutpostLoop_Data", "StreamingAssets", "windows-release.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 65536)
                throw new InvalidOperationException("正式 Player 缺少 PC 发布身份。");
            var actual = JsonUtility.FromJson<WindowsUpdateManifest>(File.ReadAllText(path));
            if (actual == null || !actual.ValidIdentity || actual.releaseNumber != identity.releaseNumber ||
                actual.versionName != identity.versionName)
                throw new InvalidOperationException("Player 发布身份与本次构建不一致。");
            foreach (string file in new[] { "OutpostLoop.exe", "UnityPlayer.dll", "GameAssembly.dll" })
                if (!File.Exists(Path.Combine(playerDirectory, file)))
                    throw new InvalidOperationException("正式 Player 缺少必需文件：" + file);
        }

        /// <summary>Player 校验及符号分离成功后提交递增序号；安装器从 Player 身份读取，不再递增。</summary>
        public void Commit()
        {
            WriteAtomically(Ledger, Encoding.UTF8.GetBytes(JsonUtility.ToJson(identity, true)));
            synchronize(Ledger);
            committed = true;
        }

        public void Dispose()
        {
            if (committed) return;
            Restore();
        }

        private void Restore()
        {
            WriteAtomically(Ledger, previousLedger);
            if (previousEmbedded == null) File.Delete(Embedded);
            else WriteAtomically(Embedded, previousEmbedded);
            synchronize(Embedded);
            synchronize(Ledger);
        }

        /// <summary>
        /// Unity 可能持有 TextAsset 的只读映射，禁止原地截断。完整写入同目录临时文件后替换目录项，
        /// 旧读取者继续持有旧映射，新读取者只看到完整身份；保留旁边的 .meta 与资产 GUID。
        /// </summary>
        private static void WriteAtomically(string path, byte[] bytes)
        {
            // 未变更的台账不重写，尤其避免准备阶段失败时无谓触碰共享台账。
            if (File.Exists(path))
            {
                byte[] current = File.ReadAllBytes(path);
                bool same = current.Length == bytes.Length;
                for (int i = 0; same && i < current.Length; i++) same = current[i] == bytes[i];
                if (same) return;
            }
            string temporary = Path.Combine(Path.GetDirectoryName(path),
                "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
#endif
