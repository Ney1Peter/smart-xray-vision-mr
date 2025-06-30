using Meta.XR.MRUtilityKit;
using System.Collections.Generic;
using UnityEngine;

public class MyEffectMeshManager : MonoBehaviour
{
    public Material defaultMaterial;
    public bool generateColliders = true;
    public int layer = 0;

    private Dictionary<MRUKAnchor, MyEffectMeshObject> effectMeshes = new();

    void Start()
    {
        if (MRUK.Instance == null) return;

        foreach (var room in MRUK.Instance.Rooms)
        {
            foreach (var anchor in room.Anchors)
            {
                if (ShouldInclude(anchor))
                {
                    CreateEffectMesh(anchor);
                }
            }
        }
    }

    bool ShouldInclude(MRUKAnchor anchor)
    {
        return anchor.PlaneBoundary2D != null &&
               (anchor.Label & MRUKAnchor.SceneLabels.WALL_FACE) != 0;
    }

    void CreateEffectMesh(MRUKAnchor anchor)
    {
        if (effectMeshes.ContainsKey(anchor)) return;

        GameObject go = new GameObject("MyEffectMesh_" + anchor.name);
        go.transform.SetParent(anchor.transform, false);
        go.layer = layer;

        Mesh mesh = GenerateMesh(anchor);
        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.material = defaultMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        Collider collider = null;
        if (generateColliders)
        {
            if (anchor.VolumeBounds.HasValue)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = anchor.VolumeBounds.Value.size;
                box.center = anchor.VolumeBounds.Value.center;
                collider = box;
            }
            else
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                collider = mc;
            }
        }

        effectMeshes[anchor] = new MyEffectMeshObject { gameObject = go, mesh = mesh, collider = collider };
    }

    Mesh GenerateMesh(MRUKAnchor anchor)
    {
        Triangulator.TriangulatePoints(anchor.PlaneBoundary2D, null, out var vertices2D, out var triangles);

        Vector3[] vertices3D = new Vector3[vertices2D.Length];
        for (int i = 0; i < vertices2D.Length; i++)
            vertices3D[i] = new Vector3(vertices2D[i].x, vertices2D[i].y, 0);

        Mesh mesh = new Mesh();
        mesh.vertices = vertices3D;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }

    class MyEffectMeshObject
    {
        public GameObject gameObject;
        public Mesh mesh;
        public Collider collider;
    }
}
