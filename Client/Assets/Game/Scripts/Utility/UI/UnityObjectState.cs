using UnityEngine;

namespace Game
{
    /// <summary>
    /// 集中处理 Unity 对象已销毁但托管引用仍存在的状态判断。
    /// </summary>
    public static class UnityObjectState
    {
        public static bool IsMissing(Object target)
        {
            return target == null;
        }
    }
}
