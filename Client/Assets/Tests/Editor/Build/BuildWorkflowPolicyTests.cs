using System;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class BuildWorkflowPolicyTests
    {
        private static Type RequiredType(string name)
        {
            Type type = Type.GetType("Game.Editor." + name + ", Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, "缺少构建策略类型：" + name);
            return type;
        }

        [Test]
        public void ResourceVersionsDistinguishSecondsWithinSameMinute()
        {
            MethodInfo method = RequiredType("BuildWorkflowUtility").GetMethod("CreateResourceVersion");
            Assert.That(method.Invoke(null, new object[] { new DateTime(2026, 10, 9, 14, 5, 1) }),
                Is.EqualTo("20261009140501"));
            Assert.That(method.Invoke(null, new object[] { new DateTime(2026, 10, 9, 14, 5, 2) }),
                Is.EqualTo("20261009140502"));
        }

        [TestCase(false, false, false, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(true, true, false, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, false, false, true)]
        public void ResumeRequiresRealReloadAndIdleEditor(
            bool reloaded,
            bool compiling,
            bool updating,
            bool expected)
        {
            MethodInfo method = RequiredType("BuildWorkflowUtility").GetMethod("CanResume");
            Assert.That(method.Invoke(null, new object[] { reloaded, compiling, updating }), Is.EqualTo(expected));
        }

        [Test]
        public void FailureIncludesWorkflowStageTimingAndRetainedArtifact()
        {
            Type timingType = RequiredType("BuildStageTiming");
            object timing = Activator.CreateInstance(timingType, "PC 完整打包");
            MethodInfo run = timingType.GetMethod("Run", new[] { typeof(string), typeof(Action) });
            Action fail = () => throw new InvalidOperationException("sample-failure");
            Assert.Throws<TargetInvocationException>(() => run.Invoke(timing, new object[] { "BuildPlayer", fail }));

            MethodInfo format = RequiredType("BuildWorkflowUtility").GetMethod("FormatFailure");
            string text = (string)format.Invoke(null, new[] { timing, "sample-failure", "E:/Build/Windows" });
            Assert.That(text, Does.Contain("入口: PC 完整打包"));
            Assert.That(text, Does.Contain("失败阶段: BuildPlayer"));
            Assert.That(text, Does.Contain("sample-failure"));
            Assert.That(text, Does.Contain("E:/Build/Windows"));
            Assert.That(text, Does.Contain("BuildPlayer:"));
        }

        [TestCase("{\"names\":[\"Game.dll\"],\"types\":[16]}", true)]
        [TestCase("{\"names\":[\"Game.dll\"],\"types\":[0]}", false)]
        [TestCase("{\"names\":[\"Other.dll\"],\"types\":[16]}", false)]
        [TestCase("{\"names\":[\"Game.dll\",\"Game.dll\"],\"types\":[16,16]}", false)]
        public void PlayerAssemblyListRequiresUniqueUserDll(string json, bool expected)
        {
            MethodInfo method = RequiredType("WindowsBuildSafety").GetMethod("TryValidateAssemblyList");
            object[] arguments = { json, new[] { "Game.dll" }, null };
            Assert.That(method.Invoke(null, arguments), Is.EqualTo(expected));
            Assert.That(arguments[2], expected ? Is.Null : Is.Not.Null);
        }
    }
}
