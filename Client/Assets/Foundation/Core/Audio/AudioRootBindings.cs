using UnityEngine;
using UnityEngine.Audio;

namespace Game
{
    /// <summary>
    /// 保存音频系统需要的根节点绑定，明确音乐和音效 AudioSource 的所有权来自场景对象。
    /// </summary>
    public sealed class AudioRootBindings : MonoBehaviour
    {
        /// <summary>项目独立混音器，提供主音量、音乐、音效分组参数。</summary>
        public AudioMixer Mixer;
        /// <summary>音乐输出分组。</summary>
        public AudioMixerGroup MusicGroup;
        /// <summary>音效输出分组。</summary>
        public AudioMixerGroup SoundGroup;
        /// <summary>双音乐源交叉淡入淡出的第一个槽。</summary>
        public AudioSource MusicSourceA;
        /// <summary>双音乐源交叉淡入淡出的第二个槽。</summary>
        public AudioSource MusicSourceB;
        /// <summary>闲置音源池的场景归属节点。</summary>
        public Transform SoundPoolRoot;
        /// <summary>当前播放音效的场景归属节点。</summary>
        public Transform PlayingSoundRoot;

        public bool IsValid => Mixer != null && MusicGroup != null && SoundGroup != null &&
            MusicSourceA != null && MusicSourceB != null && MusicSourceA != MusicSourceB &&
            SoundPoolRoot != null && PlayingSoundRoot != null;
    }
}
