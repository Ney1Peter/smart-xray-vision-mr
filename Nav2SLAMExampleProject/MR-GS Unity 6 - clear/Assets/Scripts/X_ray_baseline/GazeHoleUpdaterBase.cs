using UnityEngine;
using GaussianSplatting.Runtime;

/// <summary>
/// Use the headset camera gaze to open a "cylindrical hole" in the point cloud.<br/>
/// ─ Adjustable center alpha, radius & depth.<br/>
/// ─ Each frame writes a cylinder segment into PointCloudPathClipper, then clipped/faded in Shader.<br/>
/// </summary>
[RequireComponent(typeof(Camera))]
public class GazeHoleUpdaterBase: MonoBehaviour
{
    /* ====== Inspector Parameters ====== */
    [Header("Clipper (PointCloudPathClipper)")]
    [SerializeField] PointCloudPathClipperBase clipper;

    [Header("Hole Radius (m)")]
    [SerializeField] float cutRadius = 0.30f;

    [Header("Hole Depth (m)   ← Adjustable in Inspector")]
    [SerializeField] float cutDepth = 0.10f;

    [Header("Minimum Transparency at Center (α)")]
    [Range(0f, 1f)] public float centerAlpha = 0.05f;

    [Header("Maximum Detection Distance (m)")]
    [SerializeField] float maxDistance = 10f;

    [Header("Reset Clip Segments Each Frame")]
    [SerializeField] bool clearEachFrame = true;

    /* ====== Public Static Values ====== */
    public static float CutRadius { get; private set; }
    public static float CutDepth { get; private set; }

    /* ====== Internal ====== */
    Camera cam;
    int mask = ~0; // Default all layers; if cubes are on a separate layer, change this to LayerMask

    void Awake() => cam = GetComponent<Camera>();

    void Update()
    {
        /* —— Sync current Inspector values to static for other scripts to read —— */
        CutRadius = cutRadius;
        CutDepth = cutDepth;

        if (clipper == null || WallBoxBuildingBase.wallMat == null) return;
        if (clearEachFrame) clipper.ClearAll();

        // ① Gaze Ray
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (Physics.Raycast(ray, out var hit, maxDistance, mask) &&
            hit.collider.name.StartsWith("WallBox")) // Only process wall cubes
        {
            Vector3 p = hit.point;         // World position of hit point
            Vector3 back = -hit.normal * cutDepth;  // Direction of thickness

            /* ② Update Shader hole parameters (center + radius + alpha) */
            WallBoxBuildingBase.wallMat.SetVector("_CutCenterR",
                new Vector4(p.x, p.y, p.z, cutRadius));
            WallBoxBuildingBase.wallMat.SetFloat("_CutMinAlpha", centerAlpha);

            /* ③ Write the cylindrical segment to the clipper buffer */
            clipper.AddSegment(p, p + back, cutRadius);
        }
        else
        {
            // If the gaze leaves the wall, optionally disable the hole (radius = 0)
            WallBoxBuildingBase.wallMat.SetVector("_CutCenterR", Vector4.zero);
        }
    }
}
