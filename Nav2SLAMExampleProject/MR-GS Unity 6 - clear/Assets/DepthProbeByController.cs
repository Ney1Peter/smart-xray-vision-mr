using UnityEngine;
using Meta.XR;

public class DepthProbeByController : MonoBehaviour
{
    [SerializeField] EnvironmentRaycastManager raycastManager;
    [SerializeField] Transform rightController;
    [SerializeField] Transform hmd;
    [SerializeField] GameObject debugSpherePrefab;

    void Update()
    {
        // ▶▶ 只在按下“右手 B”时触发
        if (!OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
            return;

        if (!raycastManager || !rightController || !hmd) return;

        Ray ray = new Ray(rightController.position, rightController.forward);

        if (raycastManager.Raycast(ray, out var hit)
            && hit.status == EnvironmentRaycastHitStatus.Hit)
        {
            float dCtrl = Vector3.Distance(hit.point, ray.origin);
            float dHmd = Vector3.Distance(hit.point, hmd.position);

            Debug.Log(
                $"✅ 目标: {hit.point}  法线: {hit.normal}\n" +
                $"   ↳距手柄: {dCtrl:F2} m\n" +
                $"👤 玩家: {hmd.position}  朝向: {hmd.forward}\n" +
                $"   ↳距玩家: {dHmd:F2} m");

            if (debugSpherePrefab)
                Instantiate(debugSpherePrefab, hit.point, Quaternion.identity);
        }
        else
            Debug.Log("❌ 未命中");
    }
}
