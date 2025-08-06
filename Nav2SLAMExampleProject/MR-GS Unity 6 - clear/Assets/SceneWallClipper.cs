using UnityEngine;
using System.Collections;
using Meta.XR.MRUtilityKit;

public class SceneWallClipper : MonoBehaviour
{
    [SerializeField] PointCloudPathClipperRect clipper;
    [SerializeField] float radius = 0.20f;   // 与裁剪器保持一致
    [SerializeField] float thickness = 0.10f;   // 额外向前挖的厚度

    void Start()
    {
        StartCoroutine(WaitForRoomAndBuild());
    }

    IEnumerator WaitForRoomAndBuild()
    {
        // 方式 1：简单轮询
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
            yield return null;                 // 下一帧再试

        BuildWallSegments();
    }

    void BuildWallSegments()
    {
        var room = MRUK.Instance.GetCurrentRoom();
        int segCount = 0;

        foreach (var anchor in room.WallAnchors)
        {
            if (!anchor.PlaneRect.HasValue) continue;

            var rect = anchor.PlaneRect.Value;
            Vector3 c = anchor.transform.position;
            Vector3 r = anchor.transform.right;
            Vector3 up = anchor.transform.up;
            Vector3 n = anchor.transform.forward;      // 墙外法线

            float w = rect.size.x, h = rect.size.y;
            int layers = Mathf.Max(1, Mathf.CeilToInt(h / (radius * 2f)));

            for (int i = 0; i < layers; i++)
            {
                float fy = (i + 0.5f) / layers - 0.5f;
                Vector3 upOff = up * fy * h;

                Vector3 A0 = c - r * w * 0.5f + upOff;
                Vector3 B0 = c + r * w * 0.5f + upOff;
                Vector3 shift = n * thickness;
                Vector3 A1 = A0 + shift;
                Vector3 B1 = B0 + shift;

                //clipper.AddSegment(A0, B0);   // 原墙面
                //clipper.AddSegment(A1, B1);   // 前移后墙面
                segCount += 2;
            }

            // 隐藏可视化（可选）
            var mr = anchor.GetComponent<MeshRenderer>();
            if (mr) mr.enabled = false;
        }

        Debug.Log($"SceneWallClipper ▶ 已写入 {segCount} 条墙体线段");
    }
}
