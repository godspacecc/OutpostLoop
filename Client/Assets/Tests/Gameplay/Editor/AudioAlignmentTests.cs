using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using QFramework;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace OutpostLoop.Tests
{
    /// <summary>限定十项音频所有权与池化测试，使用独立临时节点和内存音频。</summary>
    public sealed class AudioAlignmentTests
    {
        private GameObject root;
        private AudioSystem audio;
        private AudioTestLoader loader;
        private IArchitecture architecture;

        [SetUp] public void Setup()
        {
            root=AudioTestRig.Create();
            loader=new AudioTestLoader();
            ResourceLoader.Use(loader);
            architecture=Game.Game.Interface;
            audio=architecture.GetSystem<AudioSystem>();
        }
        [TearDown] public void Cleanup()
        {
            architecture.Deinit();
            LifeCycle.ClearForTests();
            ResourceLoader.ResetForTests();
            Object.DestroyImmediate(root);
        }
        [Test] public void AudioFrameworkExists()
        {
            Assert.NotNull(audio);
            Assert.IsTrue(root.GetComponent<AudioRootBindings>().IsValid);
            Assert.AreEqual(8,audio.PooledSourceCount);
        }
        [Test] public void SynchronousSoundReleasesExactlyOnceAndReturnsSource()
        {
            loader.Immediate=true;
            var handle=audio.PlaySound("Audio/Sound/probe");
            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(1,audio.ActiveSoundCount);
            audio.Stop(handle);audio.Stop(handle);
            Assert.AreEqual(0,audio.ActiveSoundCount);
            Assert.AreEqual(8,audio.PooledSourceCount);
            Assert.AreEqual(1,loader.Released.Count);
        }
        [Test] public void CancelBeforeCompletionNeverResurrectsSound()
        {
            var handle=audio.PlaySound("Audio/Sound/pending");
            audio.Stop(handle);loader.Complete(0);
            Assert.AreEqual(0,audio.ActiveSoundCount);
            Assert.AreEqual(1,loader.Released.Count);
            Assert.AreEqual(8,audio.PooledSourceCount);
        }
        [Test] public void MusicCompletionAfterSceneNodesDestroyedReleasesWithoutThrowing()
        {
            audio.PlayMusic("Audio/Music/pending",0);
            Object.DestroyImmediate(root);
            Assert.DoesNotThrow(()=>loader.Complete(0));
            Assert.AreEqual(1,loader.Released.Count);
        }
        [Test] public void StaleMusicCompletionCannotReplaceLatestRequest()
        {
            audio.PlayMusic("Audio/Music/old",0);
            audio.PlayMusic("Audio/Music/new",0);
            loader.Complete(0);loader.Complete(1);
            Assert.AreEqual(1,loader.Released.Count);
            audio.PlayMusic("Audio/Music/new",0);
            Assert.AreEqual(2,loader.Requests.Count);
            audio.StopMusic(0);
            Assert.AreEqual(2,loader.Released.Count);
            Assert.AreEqual(2,loader.Released.Distinct().Count());
        }
        [Test] public void FailedMusicReplacementPreservesCurrentAndAllowsRetry()
        {
            audio.PlayMusic("Audio/Music/active",0);loader.Complete(0);
            audio.PlayMusic("Audio/Music/fail",0);loader.Complete(1,true);
            Assert.AreSame(loader.Clip,root.GetComponent<AudioRootBindings>().MusicSourceA.clip);
            audio.PlayMusic("Audio/Music/fail",0);
            Assert.AreEqual(3,loader.Requests.Count);
            audio.StopMusic(0);loader.Complete(2);
            Assert.AreEqual(3,loader.Released.Distinct().Count());
        }
        [Test] public void PoolCapsAtThirtyTwoAndOnlyHigherPriorityEvicts()
        {
            loader.Immediate=true;
            for(int i=0;i<32;i++)Assert.IsTrue(audio.PlaySound("Audio/Sound/"+i).IsValid);
            Assert.IsFalse(audio.PlaySound("Audio/Sound/rejected").IsValid);
            Assert.IsTrue(audio.PlayUiSound("Audio/Sound/priority").IsValid);
            Assert.AreEqual(32,audio.ActiveSoundCount);
            Assert.AreEqual(1,loader.Released.Count);
        }
        [Test] public void MapCleanupLeavesUiSoundAlive()
        {
            loader.Immediate=true;
            audio.PlaySoundAt("Audio/Sound/spatial",new Vector3(1,2,3));
            audio.PlayUiSound("Audio/Sound/ui");
            audio.StopMapOwnedSounds();
            Assert.AreEqual(1,audio.ActiveSoundCount);
            Assert.AreEqual(1,loader.Released.Count);
        }
        [Test] public void DeinitCancelsAllPendingRequestsExactlyOnce()
        {
            audio.PlaySound("Audio/Sound/pending");
            audio.PlayMusic("Audio/Music/pending",0);
            architecture.Deinit();
            loader.Complete(0);loader.Complete(1);
            Assert.AreEqual(2,loader.Released.Count);
            Assert.AreEqual(0,audio.ActiveSoundCount);
            Assert.AreEqual(0,audio.PooledSourceCount);
        }
        [Test] public void VolumeConversionAndMissingBindingUseSafeDefaults()
        {
            Assert.AreEqual(-80,AudioVolumeUtility.LinearToDb(0));
            Assert.AreEqual(0,AudioVolumeUtility.LinearToDb(2));
            Assert.AreEqual(-6.0206f,AudioVolumeUtility.LinearToDb(.5f),.001f);
            Object.DestroyImmediate(root);
            Assert.IsFalse(audio.PlaySound("Audio/Sound/no-root").IsValid);
            Assert.AreEqual(0,loader.Requests.Count);
        }
    }

    /// <summary>测试专用音频节点，不依赖或修改当前场景中的绑定。</summary>
    internal static class AudioTestRig
    {
        public static GameObject Create()
        {
            var root=new GameObject("AudioTestRoot");
            var b=root.AddComponent<AudioRootBindings>();
            b.Mixer=AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/OutpostAudioMixer.mixer");
            Assert.NotNull(b.Mixer);
            b.MusicGroup=b.Mixer.FindMatchingGroups("Music").Single();
            b.SoundGroup=b.Mixer.FindMatchingGroups("Sound").Single();
            Transform Child(string name){var go=new GameObject(name);go.transform.SetParent(root.transform,false);return go.transform;}
            b.SoundPoolRoot=Child("Pool");b.PlayingSoundRoot=Child("Playing");
            b.MusicSourceA=Child("MusicA").gameObject.AddComponent<AudioSource>();
            b.MusicSourceB=Child("MusicB").gameObject.AddComponent<AudioSource>();
            b.MusicSourceA.playOnAwake=false;b.MusicSourceB.playOnAwake=false;
            return root;
        }
    }
    /// <summary>可控资源加载器，记录每次加载与释放，覆盖同步和延迟回调。</summary>
    internal sealed class AudioTestLoader : IResourceLoader, IDisposable
    {
        public readonly AudioClip Clip=AudioClip.Create("Audio test",441000,1,44100,false);
        public readonly List<Action<AudioClip>> Requests=new List<Action<AudioClip>>();
        public readonly List<object> Released=new List<object>();
        public bool Immediate;
        public T LoadAsset<T>(string path) where T:Object => Clip as T;
        public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> onLoaded) where T:Object
        {
            var id=Requests.Count+1;Requests.Add(clip=>onLoaded(clip as T));
            if(Immediate)onLoaded(Clip as T);
            return new ResourceLoadHandle(id);
        }
        public void Complete(int index,bool fail=false){Requests[index](fail?null:Clip);}
        public void Release(ResourceLoadHandle handle){if(handle.IsValid)Released.Add(handle.RawHandle);}
        public void Dispose(){Object.DestroyImmediate(Clip);}
    }
}
