using Newtonsoft.Json;
using UnityEngine;

namespace Game
{
    /// <summary>稳定的感知界面分组标识，使用字段名保存到设置文件。</summary>
    internal enum PerceptionGroup { Action, Map, Body, Interaction, Equipment }

    /// <summary>位置是所在安全区可移动范围的比例，原点在左上。</summary>
    public sealed class PerceptionPlacement
    {
        [JsonProperty("visible")] public bool? Visible { get; set; }
        [JsonProperty("x")] public float? X { get; set; }
        [JsonProperty("y")] public float? Y { get; set; }
        [JsonProperty("scale")] public float? Scale { get; set; }

        internal PerceptionPlacement Clean(float x, float y) => new()
        {
            Visible = Visible ?? true,
            X = Limit(X, x, 0, 1),
            Y = Limit(Y, y, 0, 1),
            Scale = Limit(Scale, 1, .75f, 1.4f)
        };

        private static float Limit(float? value, float fallback, float min, float max) =>
            value is float number && !float.IsNaN(number) && !float.IsInfinity(number)
                ? Mathf.Clamp(number, min, max) : fallback;
    }

    /// <summary>本机 HUD 偏好，与行动存档分别读写。</summary>
    public sealed class PerceptionLayout
    {
        [JsonProperty("action")] public PerceptionPlacement Action { get; set; }
        [JsonProperty("map")] public PerceptionPlacement Map { get; set; }
        [JsonProperty("body")] public PerceptionPlacement Body { get; set; }
        [JsonProperty("interaction")] public PerceptionPlacement Interaction { get; set; }
        [JsonProperty("equipment")] public PerceptionPlacement Equipment { get; set; }

        internal PerceptionLayout Clone() => new()
        {
            Action = (Action ?? new PerceptionPlacement()).Clean(0, 0),
            Map = (Map ?? new PerceptionPlacement()).Clean(1, 0),
            Body = (Body ?? new PerceptionPlacement()).Clean(0, 1),
            Interaction = (Interaction ?? new PerceptionPlacement()).Clean(.5f, 1),
            Equipment = (Equipment ?? new PerceptionPlacement()).Clean(1, 1)
        };

        internal PerceptionPlacement Get(PerceptionGroup group) => group switch
        {
            PerceptionGroup.Action => Action,
            PerceptionGroup.Map => Map,
            PerceptionGroup.Body => Body,
            PerceptionGroup.Interaction => Interaction,
            _ => Equipment
        };
    }
}
