using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using OutpostLoop.Resources;
using OutpostLoop.Resources.Editor;

namespace OutpostLoop.Tests
{
    /// <summary>限定一项：临时静音WAV的源地址与真实收集器地址一致，结束删除自建测试资产。</summary>
    public sealed class AudioResourceTests
    {
        [Test] public void AudioSourceAndCollectorShareFullLogicalAddress()
        {
            const string path="Assets/Game/Audio/Sound/AlignmentProbe.wav";
            Assert.IsFalse(File.Exists(path),"不得覆盖已有资源");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var writer=new BinaryWriter(File.Create(path)))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+882);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);
                    writer.Write((short)1);writer.Write((short)1);writer.Write(44100);writer.Write(88200);
                    writer.Write((short)2);writer.Write((short)16);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(882);writer.Write(new byte[882]);
                }
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var clip=new EditorSourceResourceLoader().LoadAsset<AudioClip>("Audio/Sound/AlignmentProbe");
                Assert.NotNull(clip);
                var settings=AssetDatabase.LoadAssetAtPath<YooAsset.Editor.AssetBundleCollectorSetting>(ResourceSourceAudit.SettingPath);
                var result=settings.BeginCollect(ResourcePackageRuntime.PackageName,true,false);
                var entry=result.CollectAssets.Single(x=>x.Address=="Audio/Sound/AlignmentProbe");
                Assert.AreEqual(path,entry.AssetInfo.AssetPath);CollectionAssert.Contains(entry.AssetTags,"startup");
            }
            finally{AssetDatabase.DeleteAsset(path);}
        }
    }
}
