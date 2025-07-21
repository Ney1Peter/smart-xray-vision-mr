using UnityEngine;

/// <summary>
/// 每帧从摄像机视线发射射线，命中墙体方块时动态裁剪对应点云区域
/// </summary>
public class GazeHoleUpdater : MonoBehaviour
{
    [Header("裁剪器（必须）")]
    public PointCloudPathClipper clipper;

    [Header("洞半径 (m)")]
    public float cutRadius = 0.5f;

    [Header("洞厚度 (m)")]
    public float cutDepth = 0.3f;

    [Header("是否每帧清空再添加")]
    public bool clearEachFrame = true;

    Camera cam;
    int mask;

    void Start()
    {
        cam = GetComponent<Camera>();
        mask = LayerMask.GetMask("Default");  // 确保墙体方块处于 Default 层或设定好的层
    }

    void Update()
    {
        if (clipper == null || cam == null) return;

        if (clearEachFrame) clipper.ClearAll();

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out var hit, 10f, mask))
        {
            Vector3 A = hit.point;
            Vector3 B = A + cam.transform.forward * cutDepth;

            clipper.AddSegment(A, B, cutRadius);
            Debug.DrawLine(A, B, Color.green, 0.1f);
        }
    }
}
