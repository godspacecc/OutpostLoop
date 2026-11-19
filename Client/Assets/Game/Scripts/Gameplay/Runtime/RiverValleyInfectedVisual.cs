using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OutpostLoop
{
    /// <summary>同步感染者骨架表现，领域仍独立决定位置、朝向和追逐状态。</summary>
    public sealed class RiverValleyInfectedVisual : MonoBehaviour
    {
        private const float Touch = .084f, Release = .302f, Blend = .075f, AnkleHeight = .088f;
        private AnimationClipPlayable idle, walk, run;
        private AnimationMixerPlayable mixer;
        private PlayableGraph graph;
        private Leg[] legs;
        private Renderer[] skins;
        private MaterialPropertyBlock properties;
        private Func<Vector2, float> height;
        private bool fast, hasPosition;
        private Vector3 previousPosition;
        private float chasePhase, walkTime, idleTime, movingWeight, chaseWeight;

        private static readonly int Ground = Shader.PropertyToID("_GroundY");

        /// <summary>使用当前河谷镇几何为落脚点取高度，禁止根运动改写领域位置。</summary>
        internal void Initialize(bool isFast, Func<Vector2, float> groundHeight, Material material,
            AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip)
        {
            fast = isFast;
            height = groundHeight;
            var animator = GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            legs = new[]
            {
                new Leg(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
                new Leg(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot)
            };
            skins = GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var skin in skins) skin.sharedMaterial = material;
            properties = new MaterialPropertyBlock();

            graph = PlayableGraph.Create("河谷镇感染者");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 3);
            idle = Bind(idleClip, 0);
            walk = Bind(walkClip, 1);
            run = Bind(runClip, 2);
            AnimationPlayableOutput.Create(graph, "感染者表现", animator).SetSourcePlayable(mixer);
            graph.Play();
            idle.SetTime(isFast ? .17f : 0f);
        }

        private AnimationClipPlayable Bind(AnimationClip clip, int slot)
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            graph.Connect(playable, 0, mixer, slot);
            return playable;
        }

        /// <summary>实测相邻领域位置的平面距离驱动追逐相位，转向和重置时释放旧落点。</summary>
        internal void Present(Vector3 position, Vector2 facing, bool engaged, bool snap, float dt)
        {
            var delta = Vector3.ProjectOnPlane(position - previousPosition, Vector3.up);
            float distance = delta.magnitude;
            bool reset = snap || !hasPosition || distance > .65f;
            bool moving = !reset && distance > .001f;
            var direction = moving ? delta.normalized : new Vector3(facing.x, 0, facing.y);
            var rotation = direction.sqrMagnitude > .01f ? Quaternion.LookRotation(direction) : transform.rotation;
            bool turn = Quaternion.Angle(transform.rotation, rotation) > 25f;
            if (reset || turn || !moving || !engaged)
                foreach (var leg in legs) leg.Contact = false;
            if (reset)
            {
                chasePhase = walkTime = idleTime = movingWeight = chaseWeight = 0;
            }
            previousPosition = position;
            hasPosition = true;
            transform.SetPositionAndRotation(position, rotation);

            idleTime += dt;
            if (moving) walkTime += dt;
            float cycle = fast ? .49f : .62f;
            float speed = fast ? 5.35f : 4.15f;
            if (moving && engaged) chasePhase = Mathf.Repeat(chasePhase + distance / (cycle * speed), 1f);
            movingWeight = Mathf.MoveTowards(movingWeight, moving ? 1f : 0f, dt * 12f);
            chaseWeight = Mathf.MoveTowards(chaseWeight, engaged && moving ? 1f : 0f, dt * 10f);
            idle.SetTime(idleTime % idle.GetAnimationClip().length);
            walk.SetTime(walkTime % walk.GetAnimationClip().length);
            run.SetTime(chasePhase * run.GetAnimationClip().length * .5f);
            mixer.SetInputWeight(0, 1f - movingWeight);
            mixer.SetInputWeight(1, movingWeight * (1f - chaseWeight));
            mixer.SetInputWeight(2, movingWeight * chaseWeight);
            graph.Evaluate(0);

            if (moving && engaged)
            {
                Plant(legs[0], chasePhase, -.155f, position, rotation, speed * cycle);
                Plant(legs[1], Mathf.Repeat(chasePhase - .5f, 1f), .155f, position, rotation, speed * cycle);
            }
            properties.SetFloat(Ground, position.y);
            foreach (var skin in skins) skin.SetPropertyBlock(properties);
        }

        private void Plant(Leg leg, float phase, float width, Vector3 position, Quaternion rotation, float stride)
        {
            float plant = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Touch - Blend, Touch, phase)) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Release, Release + Blend, phase)));
            if (plant > 0f && !leg.Contact)
            {
                var anchor = position + rotation * new Vector3(width, 0, ((Touch + Release) * .5f - phase) * stride);
                anchor.y = height(new Vector2(anchor.x, anchor.z)) + AnkleHeight;
                leg.Anchor = anchor;
                leg.Orientation = leg.Foot.rotation;
            }
            leg.Contact = plant > 0f;

            var local = Quaternion.Inverse(rotation) * (leg.Foot.position - position);
            float swing = Mathf.InverseLerp(Release, Touch + 1f, phase < Touch ? phase + 1f : phase);
            local.y = AnkleHeight + Mathf.Max(0f, local.y - AnkleHeight) * (fast ? 1f : .76f)
                + (fast ? .22f : .14f) * Mathf.Sin(Mathf.PI * swing);
            local.z *= fast ? 1.44f : 1.29f;
            local.x = Mathf.Lerp(local.x, Mathf.Sign(width) * .195f, .65f);
            var target = Vector3.Lerp(position + rotation * local, leg.Anchor, plant);
            var orientation = Quaternion.Slerp(leg.Foot.rotation, leg.Orientation, plant);
            leg.Solve(Vector3.Lerp(leg.Foot.position, target, chaseWeight),
                Quaternion.Slerp(leg.Foot.rotation, orientation, chaseWeight));
        }

        private void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        /// <summary>用原骨长和动画的膝弯方向解析求解两段腿骨。</summary>
        private sealed class Leg
        {
            private readonly Transform thigh, knee;
            private readonly float upper, lower;
            internal readonly Transform Foot;
            internal bool Contact;
            internal Vector3 Anchor;
            internal Quaternion Orientation = Quaternion.identity;

            internal Leg(Animator animator, HumanBodyBones hip, HumanBodyBones joint, HumanBodyBones foot)
            {
                thigh = animator.GetBoneTransform(hip);
                knee = animator.GetBoneTransform(joint);
                Foot = animator.GetBoneTransform(foot);
                upper = Vector3.Distance(thigh.position, knee.position);
                lower = Vector3.Distance(knee.position, Foot.position);
            }

            internal void Solve(Vector3 target, Quaternion orientation)
            {
                var hip = thigh.position;
                var offset = target - hip;
                if (offset.sqrMagnitude < .000001f) return;
                var axis = offset.normalized;
                float reach = Mathf.Clamp(offset.magnitude, Mathf.Abs(upper - lower) + .002f, upper + lower - .003f);
                var bend = Vector3.ProjectOnPlane(knee.position - hip, axis);
                if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(thigh.forward, axis);
                float projection = (upper * upper - lower * lower + reach * reach) / (2f * reach);
                float lift = Mathf.Sqrt(Mathf.Max(0f, upper * upper - projection * projection));
                var nextKnee = hip + axis * projection + bend.normalized * lift;
                var nextFoot = hip + axis * reach;
                thigh.rotation = Quaternion.FromToRotation(knee.position - hip, nextKnee - hip) * thigh.rotation;
                knee.rotation = Quaternion.FromToRotation(Foot.position - knee.position, nextFoot - knee.position) * knee.rotation;
                Foot.rotation = orientation;
            }
        }
    }
}
