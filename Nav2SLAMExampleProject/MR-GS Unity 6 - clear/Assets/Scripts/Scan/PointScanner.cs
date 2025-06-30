using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Meta.XR;
using Appletea.Dev.PointCloud;

public class PointScanner : MonoBehaviour
{
    public enum Density : int
    {
        low = 32,
        medium = 64,
        high = 128,
        vHigh = 256,
        ultra = 512
    }
    [Header("Reference Scripts")]
    [SerializeField] private EnvironmentRaycastManager depthManager;
    [SerializeField] private PointCloudRenderer pointCloudRenderer;

    [Space(10)]
    [Header("Chunk Settings")]
    [SerializeField]
    private int chunkSize = 1;
    [SerializeField]
    private int maxPointsPerChunk = 256;
    [SerializeField]
    private int initialPoolSize = 1000;

    [Space(10)]
    [Header("Scan Settings")]
    [SerializeField]
    private float scanInterval = 1.0f;
    [SerializeField]
    private Density density = Density.medium;
    [SerializeField]
    [Tooltip("The limit is about 5m")]
    private float maxScanDistance = 5;


    [Space(10)]
    [Header("Rendering Settings")]
    [SerializeField]
    private GameObject pointPrefab;
    [SerializeField]
    private float renderingRadius = 10.0f;
    [SerializeField]
    int maxChunkCount = 15;

    [Space(10)]
    [Header("Camera Settings")]
    [SerializeField]
    private Camera mainCamera;
    [SerializeField]
    [Tooltip("Percentage of the field of view")]
    private float fovMargin = 0.9f;

    private List<GameObject> points = new List<GameObject>();
    private ChunkManager pointsData;
    private Coroutine scanCoroutine = null;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        pointsData = new ChunkManager(chunkSize, maxPointsPerChunk);
        pointCloudRenderer.Initialize(pointPrefab, initialPoolSize);
        //Invoke("StartScanRoutine", 1.0f);
    }

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.RawButton.B))
        {
            if (scanCoroutine == null)
            {
                Debug.Log("扫描开始 (按下 B 键)");
                scanCoroutine = StartCoroutine(ScanRoutine());
            }

/*            Debug.Log("PLY Output Sequence...");
            ScanAndStorePointCloud(((int)density), pointsData);
            List<Vector3> points = pointsData.GetAllPoints();
            Debug.Log("PLY Output Done!");*/
            
        }
        else if (OVRInput.GetUp(OVRInput.RawButton.B))
        {
            // 只有在当前有扫描协程在运行时才停止它
            if (scanCoroutine != null)
            {
                Debug.Log("扫描停止 (松开 B 键)");
                StopCoroutine(scanCoroutine);
                scanCoroutine = null; // 清除引用，表示已停止
            }
        }
    }

    void StartScanRoutine()
    {
        StartCoroutine(ScanRoutine());
    }

    IEnumerator ScanRoutine()
    {
        while (true)
        {
            ScanAndStorePointCloud(((int)density), pointsData);

            List<Vector3> points = pointsData.GetPointsInRadius(mainCamera.transform.position, renderingRadius, maxChunkCount);
            //List<Vector3> points = pointsData.GetAllPoints();
            pointCloudRenderer.UpdatePointCloud(points);

            yield return new WaitForSeconds(scanInterval);
        }
    }

    // Culculate Point Cloud database by the depth raycast results
    void ScanAndStorePointCloud(int sampleSize, ChunkManager pointsData)
    {
        // Generate Rays
        List<Vector2> viewSpaceCoords = GenerateViewSpaceCoords(sampleSize, sampleSize);
        List<Ray> rays = new List<Ray>();
        foreach (Vector2 i in viewSpaceCoords)
        {
            rays.Add(mainCamera.ViewportPointToRay(new Vector3(i.x, i.y, 0)));
        }

        List<EnvironmentRaycastHit> results = new List<EnvironmentRaycastHit>();
        foreach (Ray ray in rays)
        {
            EnvironmentRaycastHit result;
            depthManager.Raycast(ray, out result, maxScanDistance);

            // Cutout distant points
            if (Vector3.Distance(result.point, mainCamera.transform.position) < maxScanDistance)
                results.Add(result);
        }

        //Randomize
        ListExtensions.Shuffle(results);

        foreach (var result in results)
        {
            pointsData.AddPoint(result.point);
        }
    }

    List<Vector2> GenerateViewSpaceCoords(int xSize, int zSize)
    {
        List<Vector2> coords = new List<Vector2>();

        // Get the camera's field of view and aspect ratio
        float fovY = mainCamera.fieldOfView * fovMargin;
        float fovX = Camera.VerticalToHorizontalFieldOfView(fovY, mainCamera.aspect) * fovMargin;

        // Calculate the dimensions of the view frustum at a distance of 1 unit
        float frustumHeight = 2.0f * Mathf.Tan(fovY * 0.5f * Mathf.Deg2Rad);
        float frustumWidth = 2.0f * Mathf.Tan(fovX * 0.5f * Mathf.Deg2Rad);

        // Calculate the step sizes
        float stepX = frustumWidth / (xSize - 1);
        float stepY = frustumHeight / (zSize - 1);

        // Generate coordinates
        for (int z = 0; z < zSize; z++)
        {
            for (int x = 0; x < xSize; x++)
            {
                // Calculate normalized device coordinates (NDC)
                float ndcX = (x * stepX - frustumWidth * 0.5f) / (frustumWidth * 0.5f);
                float ndcY = (z * stepY - frustumHeight * 0.5f) / (frustumHeight * 0.5f);

                // Convert NDC to view space coordinates
                float xCoord = (ndcX + 1) * 0.5f;
                float yCoord = (ndcY + 1) * 0.5f;

                coords.Add(new Vector2(xCoord, yCoord));
            }
        }

        return coords;
    }


}
