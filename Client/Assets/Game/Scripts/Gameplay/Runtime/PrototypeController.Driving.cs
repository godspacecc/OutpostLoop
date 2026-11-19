using UnityEngine;
using UnityEngine.InputSystem;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 临时车型只呈现当前领域车辆，由行动根节点统一释放。
        private Transform survivalVehicleVisual;

        /// <summary>合并编辑器的两套键盘输入，每个方向在同一帧只取一次。</summary>
        private static Vector2 ReadDrivingInput(Keyboard keyboard)
        {
            int Read(bool current, KeyCode key) => current || Input.GetKey(key) || Input.GetKeyDown(key) ? 1 : 0;
            return new Vector2(
                Read(keyboard?.dKey.isPressed == true, KeyCode.D) - Read(keyboard?.aKey.isPressed == true, KeyCode.A),
                Read(keyboard?.wKey.isPressed == true, KeyCode.W) - Read(keyboard?.sKey.isPressed == true, KeyCode.S));
        }

        /// <summary>复用 City 民用车网格；资源缺失的检出仍能看到并操作临时车。</summary>
        private void SyncSurvivalVehicle()
        {
            if (!scavengeRun.HasVehicle) return;
            if (survivalVehicleVisual == null)
            {
                survivalVehicleVisual = new GameObject("河谷镇可驾驶民用车").transform;
                survivalVehicleVisual.SetParent(scavengeRoot, false);
                var paint = ScavengeMaterial(new Color(.31f, .48f, .57f));
                var body = Shape("民用车车身", survivalVehicleVisual, PrimitiveType.Cube,
                    Vector3.up * .7f, new Vector3(1.8f, 1.4f, 4.2f), paint);
                if (survivalCityArt?.Replace(body, "car") != true)
                {
                    body.localPosition = Vector3.up * .59f;
                    body.localScale = new Vector3(1.8f, .64f, 4.2f);
                    var dark = ScavengeMaterial(new Color(.12f, .17f, .20f));
                    Shape("车窗", survivalVehicleVisual, PrimitiveType.Cube,
                        new Vector3(0, 1.1f, -.18f), new Vector3(1.53f, .54f, 2.18f), dark);
                    for (int side = -1; side <= 1; side += 2)
                        for (int axle = -1; axle <= 1; axle += 2)
                            Shape("车轮", survivalVehicleVisual, PrimitiveType.Cube,
                                new Vector3(side * .76f, .24f, axle * 1.32f), new Vector3(.25f, .48f, .67f), dark);
                }
            }
            survivalVehicleVisual.SetPositionAndRotation(SurvivalPosition(scavengeRun.VehiclePosition, 0),
                Quaternion.Euler(0, scavengeRun.VehicleHeading, 0));
        }
    }
}
