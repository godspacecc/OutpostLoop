using System;
using System.IO;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>独立临时目录验证存储边界，不访问玩家目录。</summary>
    public sealed class StorageFoundationTests
    {
        [Serializable] public sealed class Sample { public int score; }
        private string directory;
        private JsonFileStore<Sample> store;
        [SetUp] public void Setup() { directory = Path.Combine(Path.GetTempPath(), "OutpostLoopTests", Guid.NewGuid().ToString("N")); store = new JsonFileStore<Sample>(directory); }
        [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        [Test] public void MissingDoesNotCreateDirectory() { Assert.AreEqual(SaveFileStatus.Missing, store.Read("run.json").Status); Assert.IsFalse(Directory.Exists(directory)); }
        [Test] public void TraversalIsRejected() { Assert.Throws<ArgumentException>(() => store.Save("../escape.json", new Sample())); Assert.Throws<ArgumentException>(() => store.Read("sub\\escape.json")); }
        [Test] public void CorruptedFileIsPreserved()
        {
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, "run.json"); File.WriteAllText(path, "not json");
            Assert.AreEqual(SaveFileStatus.Corrupted, store.Read("run.json").Status);
            Assert.Throws<InvalidDataException>(() => store.Save("run.json", new Sample())); Assert.AreEqual("not json", File.ReadAllText(path));
        }
        [Test] public void AtomicReplacementAndFailurePreserveLastValue()
        {
            store.Save("run.json", new Sample { score = 1 }); store.Save("run.json", new Sample { score = 2 });
            Assert.AreEqual(2, store.Read("run.json").Data.score);
            using (var locked = new FileStream(Path.Combine(directory, "run.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => store.Save("run.json", new Sample { score = 3 }));
            Assert.AreEqual(2, store.Read("run.json").Data.score); Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }
    }
}
