using UnityEngine;
using System.Collections;
using Meta.XR.MRUtilityKit;

[DefaultExecutionOrder(100)]  // 确保在 MRUK 初始化之后执行
public class SceneWallClipper : MonoBehaviour
{
    [SerializeField] PointCloudPathClipper clipper;   // Inspector 拖 scene 1

    [Tooltip("等待房间数据就绪的最大秒数")]
    [SerializeField] private float timeout = 5f;

    void Start()
    {
        StartCoroutine(WaitAndCreateWallSegments());
    }

    IEnumerator WaitAndCreateWallSegments()
    {
        float t = 0f;
        MRUKRoom room = null;

        // ▸ 等 MRUK 初始化、房间加载
        while (room == null && t < timeout)
        {
            if (MRUK.Instance != null)
            {
                room = MRUK.Instance.GetCurrentRoom();
            }
            t += Time.deltaTime;
            yield return null;
        }

        if (room == null)
        {
            Debug.LogWarning("SceneWallClipper - room not ready");
            yield break;
        }

        int added = 0;
        foreach (var anchor in room.WallAnchors)   // ← 直接用墙列表
        {
            // anchor.PlaneRect 里有长宽，中心在 anchor.transform.position
            if (!anchor.PlaneRect.HasValue) continue;

            var rect = anchor.PlaneRect.Value;
            Vector3 center = anchor.transform.position;
            Vector3 right = anchor.transform.right * rect.size.x * 0.5f;

            Vector3 a = center - right;   // 左端
            Vector3 b = center + right;   // 右端

            clipper.AddSegment(a, b);
            added++;
        }
        Debug.Log($"SceneWallClipper: 增加了 {added} 条墙体裁剪线段");
    }
}
