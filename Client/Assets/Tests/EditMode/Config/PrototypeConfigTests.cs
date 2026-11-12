using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
namespace OutpostLoop.Config.Tests
{
    /// <summary>配置真实产物读取与失败边界。</summary>
    public sealed class PrototypeConfigTests
    {
        [Test] public void GeneratedRulesPreservePrototypeValues()
        {
            var config = PrototypeConfig.Load(name => File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name)));
            Assert.AreEqual(720, config.Duration);
            Assert.AreEqual(36, config.ResourceNodeCount);
            Assert.AreEqual(3, config.MaxLevel);
            Assert.AreEqual(660, config.WaveTimes[5]);
        }
        [Test] public void MissingLoaderIsRejected() { Assert.Throws<ArgumentNullException>(() => PrototypeConfig.Load(null)); }
        [Test] public void CorruptBytesAreRejected() { Assert.Catch(() => PrototypeConfig.Load(_ => new byte[0])); }
    }
}
