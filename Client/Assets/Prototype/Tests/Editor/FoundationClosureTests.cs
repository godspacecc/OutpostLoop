using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OutpostLoop.Resources;
using QFramework;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace OutpostLoop.Tests
{
    /// <summary>验证通用基础接线和隔离存档事务，不触及玩家持久数据。</summary>
    public sealed class FoundationClosureTests
    {
        private IArchitecture architecture;
        private SaveSystem saves;
        private string folder;

        [SetUp]
        public void SetUp()
        {
            ResourceLoader.Use(new EditorSourceResourceLoader());
            LocalizationLanguages.ResetCache();
            architecture = Game.Game.Interface;
            saves = architecture.GetSystem<SaveSystem>();
            folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backup/Artifacts/FoundationClosure/Save", Guid.NewGuid().ToString("N")));
            saves.ConfigureForTests(folder);
        }

        [TearDown]
        public void TearDown()
        {
            architecture?.Deinit();
            LifeCycle.ClearForTests();
            LocalizationLanguages.ResetCache();
            ResourceLoader.Shutdown();
        }

        [Test]
        public void LanguageTablesLoadAndSwitchThroughCurrentLanguage()
        {
            var model = architecture.GetModel<LocalizationModel>();
            model.SetLanguageCode("zh-CN");
            Assert.AreEqual("重试", Loc.T("CommonRetry"));
            model.SetLanguageCode("en");
            Assert.AreEqual("Retry", Loc.T("CommonRetry"));
            Assert.AreEqual("Missing.Key", Loc.T("Missing.Key"));
        }

        [Test]
        public void ValidatorRejectsMismatchedPlaceholders()
        {
            var report = LocalizationValidator.ValidateJsonTables(new Dictionary<string, string>
            {
                ["zh-CN"] = new JObject { ["prompt"] = "剩余 {0}" }.ToString(),
                ["en"] = new JObject { ["prompt"] = "Remaining {1}" }.ToString()
            });
            Assert.IsFalse(report.IsValid);
            Assert.IsTrue(report.Issues.Any(x => x.Type == LocalizationValidationIssueType.PlaceholderMismatch));
        }

        [Test]
        public void SaveRoundTripPersistsOnlyDirtySection()
        {
            var first = new Section("first");
            var second = new Section("second");
            Assert.IsTrue(saves.RegisterSection(first, out var error), error);
            Assert.IsTrue(saves.RegisterSection(second, out error), error);
            var slot = saves.CreateSlot("测试槽", null);
            Assert.IsTrue(slot.Success, slot.Error);
            var prepared = saves.PrepareLoad(slot.Data.SaveId);
            Assert.IsTrue(prepared.Success, prepared.Error);
            Assert.IsTrue(saves.CommitPreparedSave(prepared.Data, out error), error);
            first.Value = 7;
            second.Value = 9;
            saves.MarkSectionDirty("first", "验证分区写入");
            Assert.IsTrue(saves.Flush(out error), error);
            var loaded = saves.PrepareLoad(slot.Data.SaveId);
            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(7, loaded.Data.Sections["first"].Value<int>());
            Assert.AreEqual(0, loaded.Data.Sections["second"].Value<int>());
            Assert.IsFalse(saves.Delete(slot.Data.SaveId, out _));
        }

        [Test]
        public void FailedSectionRestoreRollsBackPreviouslyAppliedSections()
        {
            var first = new Section("first") { Value = 2 };
            var second = new Section("second") { Value = 3 };
            saves.RegisterSection(first, out _);
            saves.RegisterSection(second, out _);
            var candidate = SaveGameData.Create("candidate", "测试", null, DateTime.UtcNow);
            candidate.Sections["first"] = new JValue(99);
            candidate.Sections["second"] = new JValue(-1);
            Assert.IsFalse(saves.CommitPreparedSave(candidate, out _));
            Assert.AreEqual(2, first.Value);
            Assert.AreEqual(3, second.Value);
            Assert.IsNull(architecture.GetModel<SaveModel>().CurrentSaveId);
        }

        [Test]
        public void MigrationClonesSourceAndRejectsFutureVersion()
        {
            var migrations = new SaveMigrationRegistry();
            migrations.Register(0, 1, json => { json["SaveName"] = "迁移"; return json; });
            var raw = new JObject { ["Version"] = 0 };
            Assert.IsTrue(migrations.TryMigrate(raw, out var migrated, out var error), error);
            Assert.AreEqual(1, migrated.Value<int>("Version"));
            Assert.AreEqual(0, raw.Value<int>("Version"));
            Assert.IsFalse(migrations.TryMigrate(new JObject { ["Version"] = 99 }, out _, out _));
        }

        [Test]
        public void RedPointAggregatesOnceAtFrameEndAndUpdatesOnRemoval()
        {
            var system = architecture.GetSystem<RedPointSystem>();
            Assert.IsTrue(system.Register("parent"));
            Assert.IsTrue(system.Register("child", "parent"));
            system.SetLeaf("child", true, 1);
            system.SetLeaf("child", true, 3);
            LifeCycle.LateUpdate();
            Assert.AreEqual(3, system.GetState("parent").Count);
            system.Unregister("child");
            LifeCycle.LateUpdate();
            Assert.IsFalse(system.GetState("parent").Active);
        }

        [Test]
        public void DiagnosticWhitelistReportsVersionAndRejectsMultipleCommands()
        {
            Assert.IsFalse(DebugCommandCatalog.TryParse("#version; #log.clear", out _, out _));
            Assert.IsFalse(DebugCommandCatalog.TryParse("#network.status", out _, out _));
            architecture.GetSystem<DebugCommandSystem>().ExecuteAsync("#version").GetAwaiter().GetResult();
            Assert.IsTrue(architecture.GetModel<RuntimeLogModel>().Snapshot().Any(x => x.Message.Contains(Application.version)));
        }

        /// <summary>使用整数片段验证捕获和恢复边界。</summary>
        private sealed class Section : ISaveSectionProvider
        {
            public string SectionId { get; }
            public int Value { get; set; }
            public Section(string id) { SectionId = id; }
            public bool Validate(JToken data, out string error) { error = null; return true; }
            public JToken Capture() => new JValue(Value);
            public void Restore(JToken data)
            {
                var value = data?.Value<int>() ?? 0;
                if (value < 0) throw new InvalidOperationException("恢复失败探针");
                Value = value;
            }
            public void Reset() { Value = 0; }
        }
    }
}
