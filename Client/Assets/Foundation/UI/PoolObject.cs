using UnityEngine;

namespace Game
{
    [DisallowMultipleComponent]
    /// <summary>
    /// 挂在池化实例上的轻量标记，记录所属池名和当前是否已回收。
    /// </summary>
    [AddComponentMenu("")]
    public sealed class PoolObject : MonoBehaviour
    {
        public string PoolName;
        public bool IsPooled;
    }
}
