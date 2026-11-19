using System;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>把已经发生的攻击和装填状态转换为上身动作、持物和短促反馈，不参与伤害判断。</summary>
    [DefaultExecutionOrder(120)]
    public sealed class PrototypeCombatVisual : MonoBehaviour
    {
        // 会话与暂停仅由所属控制器提供，本组件不写回领域状态。
        private Func<ScavengeRun> readRun;
        private Func<bool> isPaused;
        private Animator animator;
        private Transform hand, leftArm, rightArm, leftElbow, rightElbow, leftHand, pistol, crowbar, muzzle, impact;
        private int attackVersion;
        private float attackRemaining, recoil;
        private bool wasPistol;

        /// <summary>建立手部挂点与临时反馈物件，初始版本不会重播续玩前的攻击。</summary>
        internal void Initialize(Func<ScavengeRun> run, Func<bool> paused)
        {
            readRun=run;isPaused=paused;animator=GetComponentInChildren<Animator>();
            hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftArm=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftElbow=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightElbow=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            leftHand=animator.GetBoneTransform(HumanBodyBones.LeftHand);
            pistol=transform.Find("手枪模型");pistol.SetParent(hand,false);
            var bar=new GameObject("撬棍挂点");crowbar=bar.transform;crowbar.SetParent(hand,false);
            CalibrateCrowbarGrip();
            Part("撬杆",crowbar,PrimitiveType.Cylinder,new Vector3(0,0,.25f),new Vector3(.025f,.3f,.025f),Quaternion.Euler(90,0,0),new Color(.22f,.24f,.25f));
            Part("弯钩",crowbar,PrimitiveType.Cylinder,new Vector3(0,.04f,.54f),new Vector3(.025f,.065f,.025f),Quaternion.Euler(30,0,0),new Color(.25f,.27f,.28f));
            muzzle=Part("枪口闪光",pistol,PrimitiveType.Sphere,Vector3.zero,Vector3.one*.09f,Quaternion.identity,new Color(1,.7f,.15f));
            impact=Part("命中反馈",transform,PrimitiveType.Sphere,Vector3.zero,Vector3.one*.14f,Quaternion.identity,new Color(1,.5f,.12f));
            muzzle.gameObject.SetActive(false);impact.gameObject.SetActive(false);crowbar.gameObject.SetActive(false);
            attackVersion=run()?.SurvivalAttackVersion ?? 0;
        }

        /// <summary>复用领域方向和攻击序号；上身层叠加在已有移动层上，腿部保持行走。</summary>
        private void LateUpdate()
        {
            var run=readRun?.Invoke();if(run?.Survival==null || animator==null)return;
            var state=run.Survival;
            bool gun=state.weapon?.item==SurvivalItem.Pistol;
            bool melee=state.weapon?.item==SurvivalItem.Crowbar;
            pistol.gameObject.SetActive(gun);crowbar.gameObject.SetActive(melee);
            if(isPaused())return;
            float dt=Time.unscaledDeltaTime;
            attackRemaining=Mathf.Max(0,attackRemaining-dt);recoil=Mathf.Max(0,recoil-dt);
            if(run.SurvivalAttackVersion!=attackVersion)
            {
                attackVersion=run.SurvivalAttackVersion;attackRemaining=gun ? .15f : .8f;
                if(gun)recoil=.15f;else animator.CrossFadeInFixedTime("Combat.Melee",.04f,1,0);
                if(run.SurvivalHitPatrol>=0)
                {
                    var point=run.Patrols[run.SurvivalHitPatrol].Position;
                    impact.position=new Vector3(point.x,transform.position.y+1,point.y);
                }
            }
            if(gun && !wasPistol)animator.CrossFadeInFixedTime("Combat.Aim",.08f,1,0);
            wasPistol=gun;
            animator.SetLayerWeight(1,gun || attackRemaining>0?1:0);
            if((gun || attackRemaining>0) && state.facing.sqrMagnitude>.01f)
                transform.rotation=Quaternion.LookRotation(new Vector3(state.facing.x,0,state.facing.y));
            // 重定向瞄准姿态作为基底，后坐和换弹是显式程序骨骼覆盖，不伪称供应者成套动画。
            if(gun)
            {
                float reach=0;
                if(state.action==SurvivalAction.Reload)
                {
                    float phase=Mathf.Clamp01(1-state.actionTime/2.5f);
                    reach=Mathf.Sin(phase*Mathf.PI);
                }
                var grip=transform.position+Vector3.up*(1.35f-.22f*reach)+transform.forward*(.47f-.06f*recoil/.15f)+transform.right*.12f;
                PoseArm(rightArm,rightElbow,hand,grip);
                PoseArm(leftArm,leftElbow,leftHand,grip-transform.right*.06f-Vector3.up*(.025f+.25f*reach));
                var palm=hand.position+(hand.position-rightElbow.position).normalized*.055f;
                pistol.position=palm;
                pistol.rotation=Quaternion.LookRotation(transform.forward,Vector3.up)*Quaternion.Euler(-recoil/.15f*9,0,0);
                var mesh=pistol.GetComponent<MeshFilter>().sharedMesh;
                if(mesh.bounds.size.x>mesh.bounds.size.z)pistol.rotation*=Quaternion.Euler(0,90,0);
                muzzle.position=palm+transform.forward*.27f;
            }
            muzzle.gameObject.SetActive(gun && recoil>.10f);
            impact.gameObject.SetActive(attackRemaining>(gun ? .05f : .65f) && run.SurvivalHitPatrol>=0);
        }

        /// <summary>按绑定姿态的指根校准固定握点：手柄穿过掌心，长杆朝食指一侧延伸。</summary>
        private void CalibrateCrowbarGrip()
        {
            var index=animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var little=animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            if(index==null || little==null)
                throw new InvalidOperationException("当前角色缺少手指骨骼，不能沿用该撬棍握点配置");
            // 使用手骨局部空间保存一次，随后完全继承手腕旋转，避免世界方向叉乘造成翻转。
            var indexLocal=hand.InverseTransformPoint(index.position);
            var littleLocal=hand.InverseTransformPoint(little.position);
            var knuckles=(indexLocal+littleLocal)*.5f;
            crowbar.localPosition=knuckles*.8f;
            crowbar.localRotation=Quaternion.LookRotation((indexLocal-littleLocal).normalized,knuckles.normalized);
        }

        /// <summary>双骨解析约束使手掌稳定到握点，肘向下弯曲，不移动角色根节点。</summary>
        private static void PoseArm(Transform upper,Transform lower,Transform wrist,Vector3 target)
        {
            float a=Vector3.Distance(upper.position,lower.position),b=Vector3.Distance(lower.position,wrist.position);
            var delta=target-upper.position;float distance=Mathf.Clamp(delta.magnitude,.05f,a+b-.005f);
            var axis=delta.normalized;float along=(a*a-b*b+distance*distance)/(2*distance);
            var bend=Vector3.ProjectOnPlane(Vector3.down,axis).normalized;
            var elbow=upper.position+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(wrist.position-lower.position,target-lower.position)*lower.rotation;
        }

        /// <summary>只生成无碰撞的表现几何；撬棍用于补足包内没有的道具。</summary>
        private static Transform Part(string name,Transform parent,PrimitiveType type,Vector3 position,Vector3 scale,Quaternion rotation,Color color)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;Destroy(go.GetComponent<Collider>());
            var t=go.transform;t.SetParent(parent,false);t.localPosition=position;t.localScale=scale;t.localRotation=rotation;
            var material=new Material(Shader.Find("Standard")){color=color};go.GetComponent<Renderer>().sharedMaterial=material;
            go.AddComponent<CombatMaterialLifetime>().Material=material;return t;
        }
    }

    /// <summary>销毁会话临时材质，避免进出地图积累 GPU 资源。</summary>
    public sealed class CombatMaterialLifetime : MonoBehaviour
    {
        internal Material Material;
        private void OnDestroy(){if(Material!=null)Destroy(Material);}
    }
}
