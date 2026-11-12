using QFramework;

namespace Game
{
    /// <summary>
    /// 保存运行期音量设置，主音量、音乐、音效与静音值均按 0 到 1 的归一化语义使用。
    /// </summary>
    public sealed class AudioSettingsModel : AbstractModel
    {
        public BindableProperty<float> MasterVolume { get; } = new BindableProperty<float>(1f);
        public BindableProperty<float> MusicVolume { get; } = new BindableProperty<float>(1f);
        public BindableProperty<float> SoundVolume { get; } = new BindableProperty<float>(1f);
        public BindableProperty<bool> Mute { get; } = new BindableProperty<bool>(false);

        protected override void OnInit()
        {
        }

        public void Apply(AudioSettingsData data)
        {
            data = (data ?? AudioSettingsData.CreateDefault()).Sanitize();
            MasterVolume.Value = data.MasterVolume;
            MusicVolume.Value = data.MusicVolume;
            SoundVolume.Value = data.SoundVolume;
            Mute.Value = data.Mute;
        }

        public AudioSettingsData ToData()
        {
            return new AudioSettingsData
            {
                MasterVolume = MasterVolume.Value,
                MusicVolume = MusicVolume.Value,
                SoundVolume = SoundVolume.Value,
                Mute = Mute.Value
            }.Sanitize();
        }
    }
}
