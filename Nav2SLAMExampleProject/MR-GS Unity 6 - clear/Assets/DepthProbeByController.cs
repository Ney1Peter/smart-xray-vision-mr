using UnityEngine;
using Meta.XR;

public class DepthProbeByController : MonoBehaviour
{
    [SerializeField] EnvironmentRaycastManager raycastManager;
    [SerializeField] Transform rightController;
    [SerializeField] Transform hmd;
    [SerializeField] PointCloudPathClipper clipper;      // ← 拖 scene 1

    void Update()
    {
        // 右手 B：添加线段
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
            TryPickPoint();

        // 左手 X：清空
        if (OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch))
            clipper.ClearAll();
    }

    void TryPickPoint()
    {
        if (!raycastManager || !rightController || !hmd) return;

        Ray ray = new Ray(rightController.position, rightController.forward);
        if (raycastManager.Raycast(ray, out var hit) &&
            hit.status == EnvironmentRaycastHitStatus.Hit)
        {
            Vector3 playerPos = hmd.position;
            Vector3 targetPos = hit.point;

            clipper.AddSegment(playerPos, targetPos);
            Debug.Log($"Add clip segment: {playerPos:F2} → {targetPos:F2}");
        }
    }
}
