using UnityEngine;

public class GS_ClipTest : MonoBehaviour
{
    [Header("裁剪组件（挂有 PointCloudPathClipper）")]
    public PointCloudPathClipper clipper;

    [Header("裁剪半径")]
    public float radius = 0.5f;

    [Header("线段长度")]
    public float length = 0.3f;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (clipper == null)
            {
                Debug.LogError("❌ ClipTest ▶ 缺少 PointCloudPathClipper 引用");
                return;
            }

            Vector3 A = Camera.main.transform.position + Camera.main.transform.forward * 1.0f;
            Vector3 B = A + Camera.main.transform.forward * length;

            clipper.AddSegment(A, B, radius);
            Debug.Log($"✅ ClipTest ▶ 添加裁剪段：A={A:F2}, B={B:F2}, r={radius}");
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            clipper.ClearAll();
            Debug.Log("🧹 ClipTest ▶ 清除所有裁剪段");
        }
    }
}
