using UnityEngine;
using GaussianSplatting.Runtime;

/// <summary>
/// 用头显摄像机视线对点云开“圆柱形洞”。<br/>
/// ─ 中心 α 可调、半径 & 厚度可调。<br/>
/// ─ 每帧把圆柱段写进 PointCloudPathClipper，Shader 再做裁剪/渐隐。<br/>
/// </summary>
[RequireComponent(typeof(Camera))]
public class GazeHoleUpdater : MonoBehaviour
{
    /* ====== Inspector 参数 ====== */
    [Header("裁剪器 (PointCloudPathClipper)")]
    [SerializeField] PointCloudPathClipper clipper;

    [Header("洞半径 (m)")]
    [SerializeField] float cutRadius = 0.30f;

    [Header("洞厚度 (m)   ← Inspector 可直接调")]
    [SerializeField] float cutDepth = 0.10f;

    [Header("洞中心最小透明度 (α)")]
    [Range(0f, 1f)] public float centerAlpha = 0.05f;

    [Header("最大检测距离 (m)")]
    [SerializeField] float maxDistance = 10f;

    [Header("每帧重置裁剪段")]
    [SerializeField] bool clearEachFrame = true;

    /* ====== 对外静态值 ====== */
    public static float CutRadius { get; private set; }
    public static float CutDepth { get; private set; }

    /* ====== 内部 ====== */
    Camera cam;
    int mask = ~0;                   // 默认所有层；若方块单独设 Layer，可在此改为 LayerMask

    void Awake() => cam = GetComponent<Camera>();

    void Update()
    {
        /* —— 把当前 Inspector 调整同步到静态，供其他脚本读取 —— */
        CutRadius = cutRadius;
        CutDepth = cutDepth;

        if (clipper == null || WallBoxBuilding.wallMat == null) return;
        if (clearEachFrame) clipper.ClearAll();

        // ① 视线射线
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (Physics.Raycast(ray, out var hit, maxDistance, mask) &&
            hit.collider.name.StartsWith("WallBox"))           // 只处理墙方块
        {
            Vector3 p = hit.point;         // 命中点世界坐标
            Vector3 back = -hit.normal * cutDepth;  // 厚度方向

            /* ② 更新 Shader 漏洞参数（中心+半径+α） */
            WallBoxBuilding.wallMat.SetVector("_CutCenterR",
                new Vector4(p.x, p.y, p.z, cutRadius));
            WallBoxBuilding.wallMat.SetFloat("_CutMinAlpha", centerAlpha);

            /* ③ 把圆柱段写入裁剪缓冲 */
            clipper.AddSegment(p, p + back, cutRadius);
        }
        else
        {
            // 若视线离开墙，可选择关闭洞（半径 = 0）
            WallBoxBuilding.wallMat.SetVector("_CutCenterR", Vector4.zero);
        }
    }
}
