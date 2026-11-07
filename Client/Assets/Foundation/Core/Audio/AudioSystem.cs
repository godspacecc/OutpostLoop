using System.Collections.Generic;
using QFramework;
using UnityEngine;
using UnityEngine.Audio;

namespace Game
{
    /// <summary>
    /// 管理音乐和音效播放、音量应用和地图归属声音清理；资源 Handle 由播放句柄持有并在停止时释放。
    /// </summary>
    public sealed class AudioSystem : AbstractSystem
    {
        /// <summary>预先创建的闲置音源数量，与参考框架一致。</summary>
        private const int PrewarmSoundSources = 8;
        /// <summary>音效并发上限，达到上限后按优先级决定是否替换。</summary>
        private const int MaxSoundSources = 32;

        private sealed class PlayingSound
        {
            public AudioPlaybackHandle PlaybackHandle;
            public ResourceLoadHandle ResourceHandle;
            public bool ResourceHandleAssigned;
            public bool ReleaseWhenAssigned;
            public bool Released;
            public AudioSource Source;
            public bool MapOwned;
            public bool Spatial;
            public bool UiSound;
            public bool PausedBySystem;
            public int Priority;
        }

        private sealed class MusicSlot
        {
            public AudioSource Source;
            public MusicLoadRequest Request;
            public ResourceLoadHandle Handle;
            public string Address;
            public float Fade = 1f;
        }

        private sealed class MusicLoadRequest
        {
            public int Generation;
            public string Address;
            public ResourceLoadHandle Handle;
            public bool HandleAssigned;
            public bool ReleaseWhenAssigned;
            public bool Released;
            public MusicSlot OwnerSlot;
        }

        /// <summary>当前音效记录，持有播放与资源句柄的唯一所有权。</summary>
        private readonly Dictionary<long, PlayingSound> sounds = new Dictionary<long, PlayingSound>();
        /// <summary>闲置音源队列，播放结束后重置并归还。</summary>
        private readonly Queue<AudioSource> sourcePool = new Queue<AudioSource>();
        private long nextId;
        /// <summary>音乐请求代次，拒绝过期的异步完成回调。</summary>
        private int musicGeneration;
        private string currentMusicAddress;
        private MusicSlot musicSlotA;
        private MusicSlot musicSlotB;
        private MusicSlot activeMusicSlot;
        private MusicLoadRequest pendingMusicRequest;
        private Transform soundPoolRoot;
        private Transform playingRoot;
        private AudioMixer mixer;
        private AudioMixerGroup musicGroup;
        private AudioMixerGroup soundGroup;
        private bool bindingsValid;
        /// <summary>持续保存会话暂停状态，异步完成的普通音效也遵守该状态。</summary>
        private bool sessionPaused;
        private float musicFadeAStart;
        private float musicFadeBStart;
        private float musicFadeATarget = 1f;
        private float musicFadeBTarget = 1f;
        private float musicFadeDuration;
        private float musicFadeElapsed;
        private bool musicFadeStopSilentSources;
        private static bool forceMissingBindingsForTests;

        public int ActiveSoundCount => sounds.Count;
        public int PooledSourceCount => sourcePool.Count;

        public static void SetBindingsUnavailableForTests(bool unavailable)
        {
            forceMissingBindingsForTests = unavailable;
        }

        protected override void OnInit()
        {
            LoadBindings();
            ApplyVolumes();
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(Update);
            StopMusic(0f);
            foreach (var sound in new List<PlayingSound>(sounds.Values))
            {
                Stop(sound.PlaybackHandle);
            }

            while (sourcePool.Count > 0)
            {
                var source = sourcePool.Dequeue();
                if (source != null)
                {
                    DestroyAudioObject(source.gameObject);
                }
            }

            pendingMusicRequest = null;
            musicSlotA = null;
            musicSlotB = null;
            activeMusicSlot = null;
        }

        public void SetMixerForTests(AudioMixer mixer)
        {
            this.mixer = mixer;
            ApplyVolumes();
        }

        /// <summary>把当前设置写入混音器，并保持音乐淡入淡出权重。</summary>
        public void ApplyVolumes()
        {
            var settings = this.GetModel<AudioSettingsModel>().ToData();
            var master = settings.Mute ? 0f : settings.MasterVolume;
            var music = settings.Mute ? 0f : settings.MusicVolume;
            var sound = settings.Mute ? 0f : settings.SoundVolume;
            if (mixer != null)
            {
                mixer.SetFloat("MasterVolumeDb", AudioVolumeUtility.LinearToDb(master));
                mixer.SetFloat("MusicVolumeDb", AudioVolumeUtility.LinearToDb(settings.MusicVolume));
                mixer.SetFloat("SoundVolumeDb", AudioVolumeUtility.LinearToDb(settings.SoundVolume));
            }

            ApplyMusicVolumes(mixer != null ? 1f : master * music);
            foreach (var soundEntry in sounds.Values)
            {
                if (soundEntry.Source != null)
                {
                    soundEntry.Source.volume = mixer != null ? 1f : master * sound;
                }
            }
        }

        /// <summary>
        /// 加载并切换背景音乐；新资源句柄归音乐槽持有，旧槽在淡出完成后释放。
        /// </summary>
        public void PlayMusic(string address, float fadeSeconds = 0.5f)
        {
            EnsureSceneBindings();
            if (string.IsNullOrWhiteSpace(address) || address == currentMusicAddress ||
                (pendingMusicRequest != null && pendingMusicRequest.Address == address))
            {
                return;
            }

            if (!bindingsValid)
            {
                Log.Warning("[AudioSystem] 音乐播放请求已忽略：AudioRootBindings 缺失或无效。");
                return;
            }

            CancelPendingMusicRequest();
            var requestGeneration = ++musicGeneration;
            var request = new MusicLoadRequest
            {
                Generation = requestGeneration,
                Address = address
            };
            pendingMusicRequest = request;
            var loadedSynchronously = false;
            var returnedHandle = ResourceLoader.LoadAssetAsync<AudioClip>(address, clip =>
            {
                loadedSynchronously = true;
                if (clip == null)
                {
                    if (pendingMusicRequest == request)
                    {
                        pendingMusicRequest = null;
                    }

                    ReleaseMusicRequest(request);
                    return;
                }

                if (pendingMusicRequest != request || requestGeneration != musicGeneration)
                {
                    ReleaseMusicRequest(request);
                    return;
                }

                var nextSlot = activeMusicSlot == musicSlotA ? musicSlotB : musicSlotA;
                // 加载期间场景可能已经卸载；只释放本请求，不访问销毁的音源。
                if (playingRoot == null || soundPoolRoot == null || nextSlot?.Source == null)
                {
                    pendingMusicRequest = null;
                    ReleaseMusicRequest(request);
                    return;
                }
                ReleaseMusicSlot(nextSlot);
                request.OwnerSlot = nextSlot;
                nextSlot.Request = request;
                nextSlot.Handle = request.Handle;
                nextSlot.Address = request.Address;
                nextSlot.Source.clip = clip;
                nextSlot.Source.loop = true;
                nextSlot.Source.spatialBlend = 0f;
                nextSlot.Source.outputAudioMixerGroup = musicGroup;
                SetMusicFade(nextSlot, fadeSeconds > 0f ? 0f : 1f);
                ApplyMusicVolumes();
                nextSlot.Source.Play();
                if (activeMusicSlot != null && activeMusicSlot != nextSlot && fadeSeconds <= 0f)
                {
                    ReleaseMusicSlot(activeMusicSlot);
                }

                BeginMusicFade(nextSlot, activeMusicSlot, fadeSeconds, true);
                activeMusicSlot = nextSlot;
                currentMusicAddress = address;
                pendingMusicRequest = null;
            });
            request.Handle = returnedHandle;
            request.HandleAssigned = true;
            if (request.ReleaseWhenAssigned)
            {
                ReleaseMusicRequest(request);
            }
            else if (loadedSynchronously && request.OwnerSlot != null)
            {
                request.OwnerSlot.Handle = returnedHandle;
            }
        }

        /// <summary>取消待加载音乐，并按时长淡出或立即释放音乐槽。</summary>
        public void StopMusic(float fadeSeconds = 0.5f)
        {
            musicGeneration++;
            currentMusicAddress = null;
            CancelPendingMusicRequest();
            if (fadeSeconds > 0f && ((musicSlotA?.Source != null && musicSlotA.Source.isPlaying) || (musicSlotB?.Source != null && musicSlotB.Source.isPlaying)))
            {
                BeginMusicFade(null, null, fadeSeconds, true);
                activeMusicSlot = null;
                return;
            }

            ReleaseMusicSlot(musicSlotA);
            ReleaseMusicSlot(musicSlotB);
            activeMusicSlot = null;
        }

        private void CancelPendingMusicRequest()
        {
            if (pendingMusicRequest == null)
            {
                return;
            }

            ReleaseMusicRequest(pendingMusicRequest);
            pendingMusicRequest = null;
        }

        /// <summary>播放不受会话暂停影响的高优先级界面声音。</summary>
        public AudioPlaybackHandle PlayUiSound(string address)
        {
            return PlaySoundInternal(address, 100, false, true, Vector3.zero, false, 20f);
        }

        /// <summary>
        /// 播放普通音效并返回播放句柄；资源加载完成前取消时会延迟释放归属句柄。
        /// </summary>
        public AudioPlaybackHandle PlaySound(string address, int priority = 0)
        {
            return PlaySoundInternal(address, priority, false, false, Vector3.zero, false, 20f);
        }

        /// <summary>播放归属当前地图的空间音效，支持地图统一清理。</summary>
        public AudioPlaybackHandle PlaySoundAt(string address, Vector3 position, float maxDistance = 20f, int priority = 0)
        {
            return PlaySoundInternal(address, priority, true, false, position, true, maxDistance);
        }

        /// <summary>停止指定音效并归还音源、释放加载句柄。</summary>
        public void Stop(AudioPlaybackHandle handle)
        {
            if (!handle.IsValid || !sounds.TryGetValue(handle.Id, out var sound))
            {
                return;
            }

            sounds.Remove(handle.Id);
            if (sound.Source != null)
            {
                ReturnSource(sound.Source);
            }

            ReleaseSoundResource(sound);
        }

        /// <summary>只清理地图归属音效，保留界面及普通全局声音。</summary>
        public void StopMapOwnedSounds()
        {
            foreach (var sound in new List<PlayingSound>(sounds.Values))
            {
                if (sound.MapOwned)
                {
                    Stop(sound.PlaybackHandle);
                }
            }
        }

        private AudioPlaybackHandle PlaySoundInternal(string address, int priority, bool mapOwned, bool uiSound, Vector3 position, bool spatial, float maxDistance)
        {
            EnsureSceneBindings();
            if (string.IsNullOrWhiteSpace(address) || !bindingsValid)
            {
                if (!bindingsValid)
                {
                    Log.Warning("[AudioSystem] 音效播放请求已忽略：AudioRootBindings 缺失或无效。");
                }

                return AudioPlaybackHandle.Empty;
            }

            if (sounds.Count >= MaxSoundSources)
            {
                PlayingSound lowest = null;
                foreach (var sound in sounds.Values)
                {
                    if (lowest == null || sound.Priority < lowest.Priority)
                    {
                        lowest = sound;
                    }
                }

                if (lowest == null || priority <= lowest.Priority)
                {
                    return AudioPlaybackHandle.Empty;
                }

                Stop(lowest.PlaybackHandle);
            }

            var handle = new AudioPlaybackHandle(++nextId);
            var playing = new PlayingSound
            {
                PlaybackHandle = handle,
                MapOwned = mapOwned,
                Spatial = spatial,
                UiSound = uiSound,
                Priority = priority
            };
            sounds[handle.Id] = playing;
            var returnedHandle = ResourceLoader.LoadAssetAsync<AudioClip>(address, clip =>
            {
                if (!sounds.TryGetValue(handle.Id, out var live) || clip == null)
                {
                    sounds.Remove(handle.Id);
                    ReleaseSoundResource(playing);
                    return;
                }

                if (!bindingsValid || playingRoot == null || soundPoolRoot == null) { Stop(handle); return; }

                live.Source = RentSource($"SoundSource-{handle.Id}");
                live.Source.clip = clip;
                live.Source.loop = false;
                live.Source.spatialBlend = spatial ? 1f : 0f;
                live.Source.volume = CurrentSoundVolume();
                live.Source.transform.position = position;
                live.Source.maxDistance = maxDistance;
                live.Source.outputAudioMixerGroup = soundGroup;
                live.Source.Play();
                if (sessionPaused && !live.UiSound)
                {
                    live.Source.Pause();
                    live.PausedBySystem = true;
                }
            });
            playing.ResourceHandle = returnedHandle;
            playing.ResourceHandleAssigned = true;
            if (playing.ReleaseWhenAssigned)
            {
                ReleaseSoundResource(playing);
            }

            return handle;
        }

        private void Update()
        {
            UpdateMusicFade();
            foreach (var sound in new List<PlayingSound>(sounds.Values))
            {
                if (sound.Source != null && !sound.Source.isPlaying && !sound.PausedBySystem)
                {
                    Stop(sound.PlaybackHandle);
                }
            }
        }

        private AudioSource RentSource(string name)
        {
            AudioSource source = null;
            while (source == null && sourcePool.Count > 0) source = sourcePool.Dequeue();
            if (source == null) source = CreateSource(name, playingRoot);
            source.name = name;
            source.transform.SetParent(playingRoot, false);
            source.gameObject.SetActive(true);
            ResetSource(source);
            return source;
        }

        private void ReturnSource(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            ResetSource(source);
            source.gameObject.SetActive(false);
            source.transform.SetParent(soundPoolRoot, false);
            sourcePool.Enqueue(source);
        }

        private static void ResetSource(AudioSource source)
        {
            source.Stop();
            source.clip = null;
            source.loop = false;
            source.volume = 1f;
            source.pitch = 1f;
            source.spatialBlend = 0f;
            source.maxDistance = 20f;
            source.outputAudioMixerGroup = null;
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
        }

        private void LoadBindings()
        {
            if (forceMissingBindingsForTests)
            {
                bindingsValid = false;
                return;
            }

            var bindings = Object.FindObjectOfType<AudioRootBindings>();
            if (bindings == null || !bindings.IsValid)
            {
                bindingsValid = false;
                return;
            }

            mixer = bindings.Mixer;
            musicGroup = bindings.MusicGroup;
            soundGroup = bindings.SoundGroup;
            soundPoolRoot = bindings.SoundPoolRoot;
            playingRoot = bindings.PlayingSoundRoot;
            musicSlotA = new MusicSlot { Source = bindings.MusicSourceA, Fade = 1f };
            musicSlotB = new MusicSlot { Source = bindings.MusicSourceB, Fade = 1f };
            musicSlotA.Source.outputAudioMixerGroup = musicGroup;
            musicSlotB.Source.outputAudioMixerGroup = musicGroup;
            bindingsValid = true;
            while (sourcePool.Count < PrewarmSoundSources)
            {
                var source = CreateSource($"PooledSoundSource-{sourcePool.Count + 1}", soundPoolRoot);
                source.gameObject.SetActive(false);
                sourcePool.Enqueue(source);
            }
        }

        /// <summary>场景重载会销毁旧音频节点；下次播放时绑定新节点，避免复用旧池的失效引用。</summary>
        private void EnsureSceneBindings()
        {
            if (playingRoot != null && soundPoolRoot != null) return;
            StopMusic(0);
            foreach (var sound in new List<PlayingSound>(sounds.Values)) Stop(sound.PlaybackHandle);
            sourcePool.Clear(); bindingsValid = false;
            LoadBindings();
        }

        private static AudioSource CreateSource(string name, Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject.AddComponent<AudioSource>();
        }

        private static void DestroyAudioObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        private float CurrentMusicVolume()
        {
            var settings = this.GetModel<AudioSettingsModel>().ToData();
            return mixer != null ? 1f : settings.Mute ? 0f : settings.MasterVolume * settings.MusicVolume;
        }

        private float CurrentSoundVolume()
        {
            var settings = this.GetModel<AudioSettingsModel>().ToData();
            return mixer != null ? 1f : settings.Mute ? 0f : settings.MasterVolume * settings.SoundVolume;
        }

        private void BeginMusicFade(MusicSlot fadeIn, MusicSlot fadeOut, float duration, bool stopSilentSources)
        {
            musicFadeAStart = musicSlotA?.Fade ?? 0f;
            musicFadeBStart = musicSlotB?.Fade ?? 0f;
            musicFadeATarget = fadeIn == musicSlotA ? 1f : 0f;
            musicFadeBTarget = fadeIn == musicSlotB ? 1f : 0f;
            if (fadeOut == musicSlotA) musicFadeATarget = 0f;
            if (fadeOut == musicSlotB) musicFadeBTarget = 0f;
            musicFadeDuration = Mathf.Max(0f, duration);
            musicFadeElapsed = 0f;
            musicFadeStopSilentSources = stopSilentSources;
            if (musicFadeDuration <= 0f)
            {
                CompleteMusicFade();
            }
        }

        private void UpdateMusicFade()
        {
            if (musicFadeDuration <= 0f)
            {
                return;
            }

            musicFadeElapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(musicFadeElapsed / musicFadeDuration);
            if (musicSlotA != null) musicSlotA.Fade = Mathf.Lerp(musicFadeAStart, musicFadeATarget, t);
            if (musicSlotB != null) musicSlotB.Fade = Mathf.Lerp(musicFadeBStart, musicFadeBTarget, t);
            ApplyMusicVolumes();
            if (t >= 1f)
            {
                CompleteMusicFade();
            }
        }

        private void CompleteMusicFade()
        {
            if (musicSlotA != null) musicSlotA.Fade = musicFadeATarget;
            if (musicSlotB != null) musicSlotB.Fade = musicFadeBTarget;
            musicFadeDuration = 0f;
            ApplyMusicVolumes();
            if (!musicFadeStopSilentSources)
            {
                return;
            }

            StopIfSilent(musicSlotA);
            StopIfSilent(musicSlotB);
        }

        private void ApplyMusicVolumes()
        {
            ApplyMusicVolumes(CurrentMusicVolume());
        }

        private void ApplyMusicVolumes(float baseVolume)
        {
            if (musicSlotA?.Source != null) musicSlotA.Source.volume = baseVolume * musicSlotA.Fade;
            if (musicSlotB?.Source != null) musicSlotB.Source.volume = baseVolume * musicSlotB.Fade;
        }

        private static void SetMusicFade(MusicSlot slot, float value)
        {
            if (slot != null)
            {
                slot.Fade = Mathf.Clamp01(value);
            }
        }

        private void StopIfSilent(MusicSlot slot)
        {
            if (slot != null && slot != activeMusicSlot && slot.Fade <= 0.001f)
            {
                ReleaseMusicSlot(slot);
            }
        }

        /// <summary>由项目会话转发暂停状态；仅暂停普通音效，UI声音与音乐保持播放。</summary>
        public void SetPaused(bool paused)
        {
            sessionPaused = paused;
            if (paused)
            {
                foreach (var sound in sounds.Values)
                {
                    if (sound.Source != null && !sound.UiSound && sound.Source.isPlaying)
                    {
                        sound.Source.Pause();
                        sound.PausedBySystem = true;
                    }
                }
            }
            else
            {
                foreach (var sound in sounds.Values)
                {
                    if (sound.Source != null && sound.PausedBySystem)
                    {
                        sound.Source.UnPause();
                        sound.PausedBySystem = false;
                    }
                }
            }
        }

        private static void ReleaseMusicRequest(MusicLoadRequest request)
        {
            if (request == null)
            {
                return;
            }

            if (request.Released)
            {
                return;
            }

            if (request.HandleAssigned)
            {
                ResourceLoader.Release(request.Handle);
                request.Released = true;
            }
            else
            {
                request.ReleaseWhenAssigned = true;
            }
        }

        private void ReleaseMusicSlot(MusicSlot slot)
        {
            if (slot == null)
            {
                return;
            }

            if (slot.Source != null)
            {
                slot.Source.Stop();
                slot.Source.clip = null;
            }

            if (slot.Request != null)
            {
                ReleaseMusicRequest(slot.Request);
            }
            else if (slot.Handle.IsValid)
            {
                ResourceLoader.Release(slot.Handle);
            }

            slot.Request = null;
            slot.Handle = ResourceLoadHandle.Empty;
            slot.Address = null;
            slot.Fade = 0f;
        }

        private static void ReleaseSoundResource(PlayingSound sound)
        {
            if (sound == null)
            {
                return;
            }

            if (sound.Released)
            {
                return;
            }

            if (sound.ResourceHandleAssigned)
            {
                ResourceLoader.Release(sound.ResourceHandle);
                sound.Released = true;
            }
            else
            {
                sound.ReleaseWhenAssigned = true;
            }
        }
    }
}
