using System.Collections;
using System.Linq;
using Game;
using QFramework;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace OutpostLoop.Tests
{
    /// <summary>限定一项真实 Boot→Main 音频验证；使用内存静音片段，不导入正式音效。</summary>
    public sealed class AudioStartupPlayTests
    {
        [UnityTest] public IEnumerator BootAudioPlaysPausesFadesAndReleases()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            for(int frame=0;frame<600 && Object.FindObjectOfType<PrototypeController>()==null;frame++)yield return null;
            for(int frame=0;frame<10;frame++)yield return null;
            Assert.IsTrue(FoundationRuntime.IsInitialized);
            var bindings=Object.FindObjectOfType<AudioRootBindings>();
            Assert.NotNull(bindings);Assert.IsTrue(bindings.IsValid);
            Assert.AreEqual(1,Object.FindObjectsOfType<AudioListener>().Length);
            var architecture=Game.Game.Interface;
            var audio=architecture.GetSystem<AudioSystem>();
            var model=architecture.GetModel<AudioSettingsModel>();
            var previous=model.ToData();
            var loader=new AudioTestLoader{Immediate=true};
            ResourceLoader.Use(loader);
            try
            {
                model.MasterVolume.Value=.5f;model.MusicVolume.Value=.25f;model.Mute.Value=false;
                audio.ApplyVolumes();yield return null;
                Assert.IsTrue(bindings.Mixer.GetFloat("MasterVolumeDb",out var db));
                Assert.AreEqual(AudioVolumeUtility.LinearToDb(.5f),db,.01f);
                model.Mute.Value=true;audio.ApplyVolumes();
                bindings.Mixer.GetFloat("MasterVolumeDb",out db);Assert.AreEqual(-80f,db,.01f);
                model.Apply(previous);audio.ApplyVolumes();

                var session=architecture.GetSystem<PrototypeSystem>();session.SetPaused(false);
                audio.PlaySound("Audio/Sound/world");audio.PlayUiSound("Audio/Sound/ui");
                var sources=bindings.PlayingSoundRoot.GetComponentsInChildren<AudioSource>();
                Assert.AreEqual(2,sources.Length);Assert.IsTrue(sources.All(x=>x.isPlaying));
                session.SetPaused(true);yield return null;
                Assert.AreEqual(1,sources.Count(x=>x.isPlaying));Assert.AreEqual(2,audio.ActiveSoundCount);
                loader.Immediate=false;
                audio.PlaySound("Audio/Sound/late-paused");loader.Complete(2);
                var delayed=bindings.PlayingSoundRoot.GetComponentsInChildren<AudioSource>().Single(x=>!sources.Contains(x));
                Assert.IsFalse(delayed.isPlaying,"暂停期间完成的普通音效不得开始播放");
                loader.Immediate=true;
                session.SetPaused(false);Assert.IsTrue(sources.All(x=>x.isPlaying));
                Assert.IsTrue(delayed.isPlaying);

                audio.PlayMusic("Audio/Music/first",0);
                audio.PlayMusic("Audio/Music/second",.05f);
                yield return new WaitForSecondsRealtime(.15f);
                Assert.AreEqual(1,new[]{bindings.MusicSourceA,bindings.MusicSourceB}.Count(x=>x.isPlaying));
                Assert.AreEqual(1,loader.Released.Count);
                model.Apply(previous);audio.ApplyVolumes();
                FoundationRuntime.Shutdown();
                Assert.AreEqual(0,audio.ActiveSoundCount);
                Assert.AreEqual(5,loader.Released.Count);
                Assert.AreEqual(5,loader.Released.Distinct().Count());
            }
            finally
            {
                if(FoundationRuntime.IsInitialized)
                {
                    model.Apply(previous);audio.ApplyVolumes();
                    FoundationRuntime.Shutdown();
                }
            }
            yield return new ExitPlayMode();
        }
    }
}
