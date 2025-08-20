using UnityEngine;
using System.Collections;
using Meta.XR.MRUtilityKit;
using GaussianSplatting.Runtime;

/// <summary>
/// Generates hidden BoxColliders for the four MRUK walls (used for raycasting),
/// and caches the point cloud material for clipping in the shader.
/// </summary>
public class WallBoxBuildingFrame : MonoBehaviour
{
    const float GAP_XY = 0.05f;          // Shrink amount on each side

    [Header("Box Thickness (m)")]
    public float boxThickness = 0.10f;   // ← Adjustable via Inspector

    public static Material wallMat;      // Used by GazeHoleUpdater to apply holes

    void Start() => StartCoroutine(WaitAndBuild());

    IEnumerator WaitAndBuild()
    {
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
            yield return null;

        BuildBoxes();
    }

    void BuildBoxes()
    {
        var room = MRUK.Instance.GetCurrentRoom();
        if (room == null) { Debug.LogError("Room not ready"); return; }

        var gs = FindObjectOfType<GaussianSplatRenderer>();
        wallMat = gs ? gs.m_MatSplats : null;

        int count = 0;
        foreach (var anchor in room.WallAnchors)
        {
            if (!anchor.PlaneRect.HasValue) continue;

            var rect = anchor.PlaneRect.Value;
            float w = Mathf.Max(0, rect.size.x - GAP_XY * 2f);
            float h = Mathf.Max(0, rect.size.y - GAP_XY * 2f);
            float z = Mathf.Max(0.01f, boxThickness);          // Use thickness from Inspector

            Vector3 c = anchor.transform.position;
            Vector3 f = anchor.transform.forward;
            Vector3 u = anchor.transform.up;

            var root = new GameObject($"WallBox_{count}");
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(c, Quaternion.LookRotation(f, u));

            var bc = root.AddComponent<BoxCollider>();
            bc.size = new Vector3(w, h, z);

            // Visual Cube (hidden by default)
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.transform.SetParent(root.transform, false);
            mesh.transform.localScale = bc.size;
            mesh.GetComponent<Renderer>().enabled = false;
            Destroy(mesh.GetComponent<Collider>());

            count++;
        }
        Debug.Log($"WallBoxBuilding ▶ Generated {count} wall BoxColliders (thickness = {boxThickness:F2}m)");
    }
}
