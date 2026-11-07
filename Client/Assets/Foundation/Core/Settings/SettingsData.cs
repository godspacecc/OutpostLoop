using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 表示持久化用户设置根对象，读取后会清洗默认值并克隆用于预览、保存和回滚隔离。
    /// </summary>
    [Serializable]
    public sealed class SettingsData
    {
        public const int CurrentVersion = 1;
        private const string LegacyCloseTopHotKeyActionId = "ui.close-top";

        private static readonly HashSet<string> RemovedHotKeyActionIds = new HashSet<string>
        {
            LegacyCloseTopHotKeyActionId
        };

        [JsonProperty("version")]
        public int Version { get; set; } = CurrentVersion;

        [JsonProperty("languageCode")]
        public string LanguageCode { get; set; } = "zh-CN";

        [JsonProperty("display")]
        public DisplaySettingsData Display { get; set; } = DisplaySettingsData.CreateDefault();

        [JsonProperty("audio")]
        public AudioSettingsData Audio { get; set; } = AudioSettingsData.CreateDefault();

        [JsonProperty("hotKeys")]
        public List<HotKeyBinding> HotKeys { get; set; } = HotKeyDefaults.Create();

        public static SettingsData CreateDefault()
        {
            return new SettingsData().Sanitize();
        }

        public SettingsData Clone()
        {
            return new SettingsData
            {
                Version = Version,
                LanguageCode = LanguageCode,
                Display = Display?.Clone(),
                Audio = Audio?.Clone(),
                HotKeys = HotKeys?.Select(item => item?.Clone()).ToList()
            }.Sanitize();
        }

        public SettingsData Sanitize()
        {
            Version = CurrentVersion;
            LanguageCode = LocalizationLanguages.FindOrDefault(LanguageCode).Code;
            Display = (Display ?? DisplaySettingsData.CreateDefault()).Sanitize();
            Audio = (Audio ?? AudioSettingsData.CreateDefault()).Sanitize();
            HotKeys = SanitizeHotKeys(HotKeys);
            return this;
        }

        public static List<HotKeyBinding> SanitizeHotKeys(IEnumerable<HotKeyBinding> bindings)
        {
            var result = new List<HotKeyBinding>();
            var seen = new HashSet<string>();

            if (bindings != null)
            {
                foreach (var binding in bindings)
                {
                    var actionId = binding?.ActionId?.Trim();
                    if (binding == null ||
                        string.IsNullOrWhiteSpace(actionId) ||
                        string.IsNullOrWhiteSpace(binding.BindingPath) ||
                        RemovedHotKeyActionIds.Contains(actionId) ||
                        !seen.Add(actionId))
                    {
                        continue;
                    }

                    result.Add(new HotKeyBinding
                    {
                        ActionId = actionId,
                        Context = binding.Context,
                        BindingPath = binding.BindingPath.Trim()
                    });
                }
            }

            foreach (var fallback in HotKeyDefaults.Create())
            {
                if (seen.Add(fallback.ActionId))
                {
                    result.Add(fallback);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// 保存持久化显示设置，缺失或异常分辨率会清洗为默认预设，窗口模式会按 Unity 支持范围归一化。
    /// </summary>
    [Serializable]
    public sealed class DisplaySettingsData
    {
        [JsonProperty("resolutionCode")]
        public string ResolutionCode { get; set; } = DisplayResolutionPresets.Default.Code;

        [JsonProperty("windowMode")]
        public FullScreenMode WindowMode { get; set; } = FullScreenMode.Windowed;

        public static DisplaySettingsData CreateDefault()
        {
            return new DisplaySettingsData();
        }

        public DisplaySettingsData Clone()
        {
            return new DisplaySettingsData
            {
                ResolutionCode = ResolutionCode,
                WindowMode = WindowMode
            };
        }

        public DisplaySettingsData Sanitize()
        {
            ResolutionCode = DisplayResolutionPresets.FindOrDefault(ResolutionCode).Code;
            WindowMode = DisplaySettingsModel.SanitizeWindowMode(WindowMode);
            return this;
        }
    }

    /// <summary>
    /// 保存持久化音频设置，音量字段在清洗时限制到 0 到 1，静音状态原样保留。
    /// </summary>
    [Serializable]
    public sealed class AudioSettingsData
    {
        [JsonProperty("masterVolume")]
        public float MasterVolume { get; set; } = 1f;

        [JsonProperty("musicVolume")]
        public float MusicVolume { get; set; } = 1f;

        [JsonProperty("soundVolume")]
        public float SoundVolume { get; set; } = 1f;

        [JsonProperty("mute")]
        public bool Mute { get; set; }

        public static AudioSettingsData CreateDefault()
        {
            return new AudioSettingsData();
        }

        public AudioSettingsData Clone()
        {
            return new AudioSettingsData
            {
                MasterVolume = MasterVolume,
                MusicVolume = MusicVolume,
                SoundVolume = SoundVolume,
                Mute = Mute
            };
        }

        public AudioSettingsData Sanitize()
        {
            MasterVolume = Mathf.Clamp01(MasterVolume);
            MusicVolume = Mathf.Clamp01(MusicVolume);
            SoundVolume = Mathf.Clamp01(SoundVolume);
            return this;
        }
    }
}
