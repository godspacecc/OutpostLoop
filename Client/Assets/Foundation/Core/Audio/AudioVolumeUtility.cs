using UnityEngine;

namespace Game
{
    /// <summary>
    /// 集中计算静音、主音量和分组音量叠加后的最终音量，保证输入被限制在合法范围。
    /// </summary>
    public static class AudioVolumeUtility
    {
        public const float MutedDb = -80f;

        public static float LinearToDb(float value)
        {
            value = Mathf.Clamp01(value);
            return value <= 0f ? MutedDb : Mathf.Log10(value) * 20f;
        }
    }
}
