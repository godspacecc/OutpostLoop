using NUnit.Framework;

namespace OutpostLoop.Tests
{
    /// <summary>仅八项纯规则测试；不运行其它类或完整测试套件。</summary>
    public class PrototypeRuleTests
    {
        [Test] public void CargoIsBoundedAndDepositConservesResources()
        {
            var s = new RunState();
            for (int i = 0; i < 12; i++) Assert.IsTrue(s.Gather(i % 2 == 0));
            Assert.IsFalse(s.Gather(false)); s.Deposit();
            Assert.AreEqual(6, s.Scrap); Assert.AreEqual(6, s.Energy);
            Assert.AreEqual(0, s.CargoScrap + s.CargoEnergy);
            s.Deposit(); Assert.AreEqual(6, s.Scrap);
        }
        [Test] public void UnaffordableUpgradeDoesNotMutate()
        {
            var s = new RunState(); Assert.IsFalse(s.Upgrade(1));
            Assert.AreEqual(0, s.Levels[1]); Assert.AreEqual(0, s.Scrap);
            Assert.IsFalse(s.Upgrade(-1)); Assert.IsFalse(s.Upgrade(3));
        }
        [Test] public void PaidUpgradeConsumesOnceAndStopsAtCap()
        {
            var s = new RunState();
            for (int n = 0; n < 40; n++) { for (int i = 0; i < 12; i++) s.Gather(i % 3 == 0); s.Deposit(); }
            int before = s.Scrap; Assert.IsTrue(s.Upgrade(1)); Assert.Less(s.Scrap, before);
            Assert.IsTrue(s.Upgrade(1)); Assert.IsTrue(s.Upgrade(1));
            before = s.Scrap; Assert.IsFalse(s.Upgrade(1)); Assert.AreEqual(before, s.Scrap);
        }
        [Test] public void PauseFreezesClockAndDamage()
        {
            var s = new RunState(); s.Tick(10, 0); s.Paused = true; s.Tick(20, 0); s.Damage(20);
            Assert.AreEqual(10, s.Elapsed); Assert.AreEqual(100, s.Health);
        }
        [Test] public void DeadlineWinsOnlyWhenFinalEnemiesAreCleared()
        {
            var won = new RunState(); won.Tick(720, 0); Assert.AreEqual(1, won.Outcome);
            var lost = new RunState(); lost.Tick(720, 1); Assert.AreEqual(-1, lost.Outcome);
        }
        [Test] public void DefeatIsTerminalAndCannotCollect()
        {
            var s = new RunState(); s.Damage(100); Assert.AreEqual(-1, s.Outcome);
            s.Tick(720, 0); Assert.AreEqual(-1, s.Outcome); Assert.IsFalse(s.Gather(false));
        }
        [Test] public void NewRunHasNoPreviousRunState()
        {
            var old = new RunState(); old.Gather(false); old.Deposit(); old.Tick(100, 0);
            var next = new RunState(); Assert.AreEqual(0, next.Elapsed); Assert.AreEqual(0, next.Scrap);
            Assert.AreEqual(100, next.Health); Assert.AreEqual(0, next.Outcome);
        }
        [Test] public void InvalidTimeAndDamageAreRejected()
        {
            var s = new RunState(); s.Tick(float.NaN, 0); s.Tick(-1, 0); s.Tick(float.PositiveInfinity, 0); s.Damage(-10);
            Assert.AreEqual(0, s.Elapsed); Assert.AreEqual(100, s.Health);
        }
    }
}
