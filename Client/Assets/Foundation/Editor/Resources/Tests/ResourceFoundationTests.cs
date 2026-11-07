using NUnit.Framework;
using OutpostLoop.Resources.Editor;
namespace OutpostLoop.Resources.Tests
{
    /// <summary>范围仅三条：真实源收集、源配置读取及非法地址拒绝；不验证运行包。</summary>
    public sealed class ResourceFoundationTests
    {
        [Test] public void ActualCollectorContainsStartupRule() { ResourceSourceAudit.Audit(); }
        [Test] public void EditorSourceReadsRealRule() { Assert.IsNotEmpty(ResourceService.LoadBytesForEditorSource("tbrule.bytes")); }
        [Test] public void EditorSourceRejectsTraversal() { Assert.Throws<System.ArgumentException>(() => ResourceService.LoadBytesForEditorSource("../tbrule.bytes")); }
    }
}
