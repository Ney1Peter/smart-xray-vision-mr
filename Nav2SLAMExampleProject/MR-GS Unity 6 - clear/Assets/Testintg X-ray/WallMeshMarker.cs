using UnityEngine;

/// <summary>
/// 实时在点云中挖渐变洞；射线只检测 “WallBox” 层的方块。
/// </summary>
[RequireComponent(typeof(Camera))]
public class GazeHoleUpdater : MonoBehaviour
{
    [Header("裁剪器 (PointCloudPathClipper)")]
    [SerializeField] PointCloudPathClipper clipper;

    [Header("洞半径 (m)")]
    [SerializeField] float cutRadius = 0.6f;

    [Header("中心最小透明度")]
    [Range(0f, 1f)]
    [SerializeField] float centerAlpha = 0.05f;

    [Header("洞厚度 (m)")]
    [SerializeField] float cutDepth = 0.25f;

    [Header("最大检测距离 (m)")]
    [SerializeField] float maxDistance = 10f;

    [Header("每帧重置裁剪段")]
    [SerializeField] bool clearEachFrame = true;

    // Shader property IDs
    static readonly int id_CutCenterR = Shader.PropertyToID("_CutCenterR");
    static readonly int id_CutMinAlpha = Shader.PropertyToID("_CutMinAlpha");

    Camera cam;
    LayerMask wallMask;

    void Awake()
    {
        cam = GetComponent<Camera>();

        // 只检测名为 "WallBox" 的层
        wallMask = LayerMask.GetMask("WallBox");
    }

    void Update()
    {
        if (clipper == null || WallBoxBuilding.wallMat == null) return;

        if (clearEachFrame) clipper.ClearAll();

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, wallMask))
        {
            // 双保险：确保真的是墙体方块
            if (!hit.collider.CompareTag("WallBox"))
                return;

            Vector3 center = hit.point;
            Vector3 n = hit.normal.normalized;

            Vector3 A = center - n * (cutDepth * 0.5f);
            Vector3 B = center + n * (cutDepth * 0.5f);

            clipper.AddSegment(A, B, cutRadius);

            WallBoxBuilding.wallMat.SetVector(id_CutCenterR,
                new Vector4(center.x, center.y, center.z, cutRadius));
            WallBoxBuilding.wallMat.SetFloat(id_CutMinAlpha, centerAlpha);
        }
        else
        {
            // 离开墙体时可选择关闭洞
            // WallBoxBuilding.wallMat.SetVector(id_CutCenterR, Vector4.zero);
        }
    }
}
