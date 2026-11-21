using System;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>读取人物位移和界面暂停状态，驱动原地动画与旧探索入口的步行转向。</summary>
    public sealed class PrototypePlayerVisual : MonoBehaviour
    {
        // 位移由领域层更新，本组件按相邻帧位移判断走跑。
        private Vector3 previousPosition;
        private Animator animator;
        private Func<bool> paused, sprinting;
        private bool hasPosition, turnWithMovement;
        private static readonly int Speed = Animator.StringToHash("Speed");

        /// <summary>绑定当前会话的只读状态，禁止 Animator 的根运动参与位置计算。</summary>
        internal void Initialize(Func<bool> isPaused, Func<bool> isSprinting, bool turnWithMovement)
        {
            paused = isPaused;
            sprinting = isSprinting;
            this.turnWithMovement = turnWithMovement;
            animator = GetComponentInChildren<Animator>();
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }

        /// <summary>同步位置后采样，瞬移和首次落地不作为移动动画。</summary>
        private void LateUpdate()
        {
            if (animator == null) return;
            var delta = transform.position - previousPosition;
            delta.y = 0;
            bool frozen = paused?.Invoke() ?? false;
            float seconds = Mathf.Max(Time.unscaledDeltaTime, .0001f);
            float metresPerSecond = hasPosition && delta.magnitude < 2 ? delta.magnitude / seconds : 0;
            animator.speed = frozen ? 0 : 1;
            // 领域中正常步速为四米每秒、冲刺为七米每秒；混合树使用动作的参考步速。
            float speed = frozen ? 0 : sprinting?.Invoke() == true
                ? Mathf.Min(3.4f, metresPerSecond / 7f * 3.4f)
                : Mathf.Min(1.4f, metresPerSecond / 4f * 1.4f);
            animator.SetFloat(Speed, speed, .08f, seconds);
            if (turnWithMovement && !frozen && metresPerSecond > .1f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(delta), 720f * seconds);
            previousPosition = transform.position;
            hasPosition = true;
        }
    }
}
