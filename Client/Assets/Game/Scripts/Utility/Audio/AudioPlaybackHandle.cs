using System;

namespace Game
{
    /// <summary>
    /// 持有一次音频播放的 AudioSource、资源 Handle 和完成回调，停止或释放时统一归还资源。
    /// </summary>
    public readonly struct AudioPlaybackHandle : IEquatable<AudioPlaybackHandle>
    {
        public static readonly AudioPlaybackHandle Empty = new AudioPlaybackHandle(0);

        internal AudioPlaybackHandle(long id)
        {
            Id = id;
        }

        internal long Id { get; }
        public bool IsValid => Id > 0;

        public bool Equals(AudioPlaybackHandle other)
        {
            return Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            return obj is AudioPlaybackHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}
