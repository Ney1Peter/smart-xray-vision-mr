using Unity.Mathematics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering; // Required for GraphicsBuffer
using GaussianSplatting.Runtime; // Required for GaussianSplatRenderer
using Meta.XR.BuildingBlocks; // Required for RoomMeshEvent
using Appletea.Dev.PointCloud; // Required for VFXPointCloudManualScanner (Make sure namespace is correct)
// using OVR; // Uncomment this if using OVRInput (Requires Oculus Integration Package)

public class ComplexRegistrationManagerV : MonoBehaviour
{
    [Header("Registration Targets")]
    [Tooltip("Register automatically against RoomMesh when it loads.")]
    [SerializeField] private bool useRoomMeshTarget = true;
    [Tooltip("Register against the scanned point cloud when the 'A' button is pressed.")]
    [SerializeField] private bool useScannerTarget = false;

    [Header("References")]
    [Tooltip("The 3D Gaussian Splatting object to be registered.")]
    [SerializeField] private GaussianSplatRenderer gaussianSourceRenderer;
    [Tooltip("Assign RoomMeshEvent if Use Room Mesh Target is enabled.")]
    [SerializeField] private RoomMeshEvent roomMeshEventTarget;
    [Tooltip("Assign VFXPointCloudManualScanner if Use Scanner Target is enabled.")]
    [SerializeField] private VFXPointCloudManualScanner scannerTarget;

    // FGR + GICP Settings (Keep as before)
    [Header("FGR + GICP Settings")]
    [SerializeField] private bool doDownsample = true;
    [SerializeField] private float voxelSize = 0.05f;
    [SerializeField] private int gicp_max_iter = 50;
    [SerializeField] private float gicp_epsilon = 1e-6f;
    [SerializeField] private bool useFGR = false;
    [SerializeField] private float voxelSizeFGR = 0.05f;
    [SerializeField] private bool isLocalRegistration = false;
    [SerializeField] private bool useVGICP = true;
    [SerializeField] private float maxDistance = 1;
    [SerializeField] private int randomCorrespondence = 20;
    [SerializeField] private float vResolution = 0.1f;




    [Space(10)]
    [Header("Selection & Interaction")]
    public Vector3 boxSize = new Vector3(2f, 2f, 2f); // Initial size of the selection sphere
    //public float moveSpeed = 1f; // Speed to move the sphere
    //public Material boxMaterial; // Material for the selection sphere
    // Runtime state
    private List<Vector3> cloudSource = new List<Vector3>(); // Moving points (3DGS)
    private bool roomMeshRegistrationAttemptedOrCompleted = false;
    private bool isRegistrationRunning = false; // Prevent concurrent registrations
    private GameObject selectionBox;
    private Material boxMaterial;
    private int splatCount;
    private List<int> selectedGSIndices = new List<int>();
    private float3[] positions;
    private int alignCount = 0;



    // --- DLL Import & Struct (Keep as before) ---
/*    [DllImport("FGR_GICP.dll", CallingConvention = CallingConvention.Cdecl)]
*/    [DllImport("VGICP.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern GICPResult RunFGRGICP(
        float[] refPoints, 
        int refTotalFloats, 
        float[] tgtPoints, 
        int tgtTotalFloats,
        bool doDownsample, 
        float voxelSize, 
        int gicp_max_iter, 
        float gicp_epsilon,
        bool useFGR,
        float voxelSizeFGR,
        bool useVGICP,
        float maxDistance,
        float vResolution,
        int randomCorrespondence);

    [StructLayout(LayoutKind.Sequential)]
    public struct GICPResult
    { /* ... as before ... */
        [MarshalAs(UnmanagedType.I1)]
        public bool converged;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] matrix;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] centroid_ref_before;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] centroid_target_before;
    }

    void Start()
    {
        // --- Initial Validation ---
        if (gaussianSourceRenderer == null)
        {
            Debug.LogError("Gaussian Source Renderer is not assigned!", this);
            enabled = false; return;
        }
        if (!useRoomMeshTarget && !useScannerTarget)
        {
            Debug.LogWarning("No registration target selected (Use Room Mesh or Use Scanner). Script will do nothing.", this);
            enabled = false; return;
        }
        if (useRoomMeshTarget && roomMeshEventTarget == null)
        {
            Debug.LogError("Use Room Mesh Target is enabled, but RoomMeshEvent Target is not assigned!", this);
            enabled = false; return;
        }
        if (useScannerTarget && scannerTarget == null)
        {
            Debug.LogError("Use Scanner Target is enabled, but VFXPointCloudManualScanner Target is not assigned!", this);
            enabled = false; return;
        }
        // Ensure scanner target has a way to provide points if needed
        if (useScannerTarget && scannerTarget.GetType().GetMethod("GetStoredPointCloud") == null && scannerTarget.GetType().GetField("pointsData") == null)
        {
            Debug.LogError("VFXPointCloudManualScanner script does not have a public 'GetStoredPointCloud()' method or a public 'pointsData' field. Cannot access scanned points.", this);
            // You need to implement one of the access methods suggested previously in VFXPointCloudManualScanner.cs
            enabled = false; return;
        }

        splatCount = gaussianSourceRenderer.splatCount;
        // --- Extract Source Point Cloud (3DGS) ---
        if (!ExtractSourcePointCloud())
        {
            enabled = false; // Stop if source extraction fails
            return;
        }

        // --- Setup based on selected targets ---
        if (useRoomMeshTarget)
        {
            Debug.Log("RoomMesh target enabled. Subscribing to load event.");
            roomMeshEventTarget.OnRoomMeshLoadCompleted.AddListener(HandleRoomMeshLoaded);
        }

        if (useScannerTarget)
        {
            Debug.Log("Scanner target enabled. Waiting for 'A' button press after any RoomMesh registration.");
            // Input handling will be done in Update()
        }


    }

    void Update()
    {
        // --- Scanner Registration Trigger ---
        // Check if Scanner Target is enabled AND the 'A' button is pressed down this frame
        // IMPORTANT: Requires Oculus Integration Package and uncommenting 'using OVR;' at the top
        // Replace OVRInput with your input system if not using Oculus SDK (e.g., Input System package)
        if (useScannerTarget && !isRegistrationRunning && OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch)) // Check Right Controller A button
        // if (useScannerTarget && !isRegistrationRunning && Input.GetKeyDown(KeyCode.A)) // Alternative for Keyboard testing
        {
            Debug.Log("'A' Button pressed. Attempting registration against scanned point cloud.");

            // If RoomMesh is also used, ensure its registration has finished first.
            if (useRoomMeshTarget && !roomMeshRegistrationAttemptedOrCompleted)
            {
                Debug.LogWarning("RoomMesh registration is pending or running. Please wait before triggering scanner registration.");
                return; // Don't proceed yet
            }

            ExtractSourcePointCloud();

            // Proceed with scanner registration
            TriggerRegistrationWithScannerData();
            
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        if (roomMeshEventTarget != null)
        {
            roomMeshEventTarget.OnRoomMeshLoadCompleted.RemoveListener(HandleRoomMeshLoaded);
        }
    }

    // --- Point Cloud Extraction ---

    private bool ExtractSourcePointCloud()
    {
        GraphicsBuffer gpuPosBuffer = gaussianSourceRenderer.GetGpuPosData();
        if (gpuPosBuffer == null)
        {
            Debug.LogError("Could not get GPU Position Buffer from Gaussian Source Renderer.", this);
            return false;
        }
        cloudSource = ExtractPointCloudFromGraphicsBuffer(gpuPosBuffer, gaussianSourceRenderer.transform);
        

        if (cloudSource.Count == 0)
        {
            Debug.LogError("Extracted source (3DGS) point cloud is empty!", this);
            return false;
        }

        Debug.Log($"Extracted {cloudSource.Count} points from 3DGS as the source point cloud.");
        return true;
    }

    private List<Vector3> ExtractRoomMeshPointCloud(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("Cannot extract RoomMesh points: MeshFilter or sharedMesh is null.");
            return new List<Vector3>();
        }
        Mesh mesh = mf.sharedMesh;
        Transform t = mf.transform;
        Vector3[] vertices = mesh.vertices;
        List<Vector3> points = new List<Vector3>(vertices.Length);
        Matrix4x4 localToWorld = t.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++)
        {
            points.Add(localToWorld.MultiplyPoint3x4(vertices[i]));
        }
        Debug.Log($"Extracted {points.Count} points from RoomMesh.");
        return points;
    }

    private List<Vector3> ExtractScannerPointCloud()
    {
        if (scannerTarget == null) return new List<Vector3>();

        // Use the public accessor method if available (Recommended)
        var getMethod = scannerTarget.GetType().GetMethod("GetStoredPointCloud");
        if (getMethod != null)
        {
            return (List<Vector3>)getMethod.Invoke(scannerTarget, null);
        }

        // Fallback to direct access if the field is public (Less Recommended)
        var fieldInfo = scannerTarget.GetType().GetField("pointsData");
        if (fieldInfo != null && fieldInfo.IsPublic)
        {
            var pointsDataObj = fieldInfo.GetValue(scannerTarget);
            if (pointsDataObj != null)
            {
                var getAllPointsMethod = pointsDataObj.GetType().GetMethod("GetAllPoints");
                if (getAllPointsMethod != null)
                {
                    return (List<Vector3>)getAllPointsMethod.Invoke(pointsDataObj, null);
                }
            }
        }

        Debug.LogError("Could not find a way to access points from VFXPointCloudManualScanner. Ensure 'GetStoredPointCloud()' or public 'pointsData' exists.", this);
        return new List<Vector3>();
    }


    // --- Registration Logic ---

    private void HandleRoomMeshLoaded(MeshFilter mf)
    {
        if (!useRoomMeshTarget || isRegistrationRunning) return; // Only if enabled and not busy

        Debug.Log("RoomMesh loaded event received.");
        List<Vector3> targetCloud = ExtractRoomMeshPointCloud(mf);

        if (targetCloud.Count > 0)
        {
            InitiateRegistrationProcess(targetCloud, "RoomMesh");
        }
        else
        {
            Debug.LogWarning("Extracted RoomMesh point cloud is empty. Skipping registration.");
            roomMeshRegistrationAttemptedOrCompleted = true; // Mark as done even if skipped
        }
        // Optionally disable renderer
        var meshRenderer = mf.gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer) meshRenderer.enabled = false;
    }

    private void TriggerRegistrationWithScannerData()
    {


        if (!useScannerTarget || isRegistrationRunning) return; // Only if enabled and not busy

        Debug.Log("Attempting to extract points from scanner...");
        List<Vector3> targetCloud = ExtractScannerPointCloud();


        if (targetCloud.Count > 0)
        {
            InitiateRegistrationProcess(targetCloud, "Scanner");
        }
        else
        {
            Debug.LogWarning("Extracted Scanner point cloud is empty! Ensure scanning was performed and data is accessible.");
        }
    }

    private void InitiateRegistrationProcess(List<Vector3> targetCloud, string targetName )
    {
        if (isRegistrationRunning)
        {
            Debug.LogWarning("Registration is already in progress.");
            return;
        }
        if (cloudSource.Count == 0)
        {
            Debug.LogError("Cannot start registration: Source (3DGS) point cloud is empty.", this);
            return;
        }
        if (targetCloud == null || targetCloud.Count == 0)
        {
            Debug.LogError($"Cannot start registration: Target ({targetName}) point cloud is empty.", this);
            // If this was the RoomMesh attempt, mark it as done so Scanner can proceed
            if (targetName == "RoomMesh") roomMeshRegistrationAttemptedOrCompleted = true;
            return;
        }

        isRegistrationRunning = true;
        Debug.Log($"Starting FGR+GICP Registration: Source ({cloudSource.Count} points) to Target '{targetName}' ({targetCloud.Count} points)");

        // Convert lists to flat float arrays
        float[] targetArray = ConvertToFloatArray(targetCloud); // Target = refPoints
        float[] sourceArray = ConvertToFloatArray(cloudSource); // Source = tgtPoints

        // --- Call the FGR+GICP DLL ---
        // Consider running this on a separate thread if it blocks the main thread for too long
        Debug.Log($"DLL Parameters: doDownsample={doDownsample}, voxelSize={voxelSize}, gicp_max_iter={gicp_max_iter}, gicp_epsilon={gicp_epsilon}, useFGR={useFGR}, voxelSizeFGR={voxelSizeFGR}");
        if (alignCount > 0 || useScannerTarget == true) useFGR = false;
        alignCount++;
        GICPResult result = RunFGRGICP(
            targetArray, targetArray.Length, sourceArray, sourceArray.Length,
            doDownsample, voxelSize, 
            gicp_max_iter, gicp_epsilon, 
            useFGR, voxelSizeFGR,useVGICP,
            maxDistance,vResolution,
            randomCorrespondence);

        // --- Process Results ---
        if (!result.converged)
        {
            Debug.LogError($"FGR+GICP registration against '{targetName}' failed to converge!");
            Matrix4x4 T = GetFinalTransformation(result); // Use exact function from previous script
            Debug.Log($"Result Matrix (Raw): {string.Join(", ", result.matrix)}");
            Debug.Log($"Transformation Matrix T:\n{T.ToString("F4")}");
            ApplyTransformationToTarget(T); // Use exact function from previous script
            Debug.Log($"Registration complete. Applied transformation to Gaussian Source Renderer based on '{targetName}'.");

        }
        else
        {
            Debug.Log($"FGR+GICP registration against '{targetName}' converged.");
            Matrix4x4 T = GetFinalTransformation(result); // Use exact function from previous script
            Debug.Log($"Result Matrix (Raw): {string.Join(", ", result.matrix)}");
            Debug.Log($"Transformation Matrix T:\n{T.ToString("F4")}");
            ApplyTransformationToTarget(T); // Use exact function from previous script
            Debug.Log($"Registration complete. Applied transformation to Gaussian Source Renderer based on '{targetName}'.");
        }

        // --- Finalize ---
        isRegistrationRunning = false;
        if (targetName == "RoomMesh")
        {
            roomMeshRegistrationAttemptedOrCompleted = true; // Mark RoomMesh step as done
            Debug.Log("RoomMesh registration process finished.");
            if (useScannerTarget) Debug.Log("Scanner registration can now be triggered with 'A' button.");
        }
        else if (targetName == "Scanner")
        {
            Debug.Log("Scanner registration process finished.");
        }
    }

    // --- Helper Functions (Copied *exactly* from ComplexRegistrationCombined.cs) ---

    private Matrix4x4 GetFinalTransformation(GICPResult result)
    { /* ... as before ... */
        float[] m = result.matrix;
        Matrix4x4 T = new Matrix4x4();
        T.m00 = m[0]; T.m01 = m[4]; T.m02 = m[8]; T.m03 = m[3];
        T.m10 = m[1]; T.m11 = m[5]; T.m12 = m[9]; T.m13 = m[7];
        T.m20 = m[2]; T.m21 = m[6]; T.m22 = m[10]; T.m23 = m[11];
        T.m30 = m[12]; T.m31 = m[13]; T.m32 = m[14]; T.m33 = m[15];
        return T;
    }
    //private void ApplyTransformationToSource(Matrix4x4 T)
    //{ /* ... as before ... */
    //    Quaternion R = ExtractRotation(T);
    //    Vector3 t = ExtractTranslation(T);
    //    Vector3 sourceOriginalPos = gaussianSourceRenderer.transform.position;
    //    Quaternion sourceOriginalRot = gaussianSourceRenderer.transform.rotation;
    //    gaussianSourceRenderer.transform.rotation = R * sourceOriginalRot;
    //    gaussianSourceRenderer.transform.position = R * sourceOriginalPos + t;
    //    Debug.Log($"Applied Transform: New Pos={gaussianSourceRenderer.transform.position}, New Rot Euler={gaussianSourceRenderer.transform.rotation.eulerAngles}");
    //}

    private void ApplyTransformationToTarget(Matrix4x4 T)
    {
        Quaternion R = ExtractRotation(T);
        Vector3 t = ExtractTranslation(T);

        Vector3 finalTranslation = t + Quaternion.Inverse(R) * gaussianSourceRenderer.transform.position;
        gaussianSourceRenderer.transform.rotation = Quaternion.Inverse(R) * gaussianSourceRenderer.transform.rotation;
        gaussianSourceRenderer.transform.position = finalTranslation;
        
        Debug.Log($"Applied Transform: New Pos={gaussianSourceRenderer.transform.position}, New Rot Euler={gaussianSourceRenderer.transform.rotation.eulerAngles}");

    }


    private Quaternion ExtractRotation(Matrix4x4 matrix)
    {
        Vector3 forward = matrix.GetColumn(2);
        Vector3 up = matrix.GetColumn(1);
        return Quaternion.LookRotation(forward, up);
    }

    private Vector3 ExtractTranslation(Matrix4x4 matrix)
    {
        return matrix.GetColumn(3);
    }
    private float[] ConvertPointCloudToFloatArray(List<Vector3> cloud)
    { /* ... as before ... */
        if (cloud == null || cloud.Count == 0) return new float[0];
        float[] arr = new float[cloud.Count * 3];
        for (int i = 0; i < cloud.Count; i++)
        {
            arr[i * 3 + 0] = cloud[i].x;
            arr[i * 3 + 1] = cloud[i].y;
            arr[i * 3 + 2] = cloud[i].z;
        }
        return arr;
    }

    float[] ConvertToFloatArray(List<Vector3> points)
    {
        float[] arr = new float[points.Count * 3];
        for (int i = 0; i < points.Count; i++)
        {
            arr[i * 3] = points[i].x;
            arr[i * 3 + 1] = points[i].y;
            arr[i * 3 + 2] = points[i].z;
        }
        return arr;
    }
    //private List<Vector3> ExtractPointCloudFromGraphicsBuffer(GraphicsBuffer buffer, Transform objTransform)
    //{
    //    int totalFloats = buffer.count;
    //    int numPoints = totalFloats / 3;
    //    float [] positions = new float[totalFloats];
    //    buffer.GetData(positions);

    //    List<Vector3> points = new List<Vector3>(numPoints);
    //    for (int i = 0; i < numPoints; i++)
    //    {
    //        int idx = i * 3;
    //        Vector3 localPoint = new Vector3(positions[idx], positions[idx + 1], positions[idx + 2]);
    //        Vector3 worldPoint = objTransform.TransformPoint(localPoint);
    //        points.Add(worldPoint);
    //    }
    //    return points;
    //}

    private List<Vector3> ExtractPointCloudFromGraphicsBuffer(GraphicsBuffer buffer, Transform objTransform)
    {
        // Step 1: 提取 float3 点云数据
        positions = new float3[splatCount];
        buffer.GetData(positions);

        // Step 2: 如果不启用 LocalRegistration，直接返回所有 Transform 后的点
        if (!isLocalRegistration)
        {
            List<Vector3> pointsAll = new List<Vector3>(splatCount);
            for (int i = 0; i < splatCount; i++)
            {
                Vector3 worldPoint = objTransform.TransformPoint(positions[i]);
                pointsAll.Add(worldPoint);
            }
            return pointsAll;
        }

        // Step 3: 启用 LocalRegistration 的处理
        CreateOrResetSelectionSphere();
        Bounds boxBounds = new Bounds(selectionBox.transform.position, selectionBox.transform.localScale);

        List<Vector3> pointsSelected = new List<Vector3>(splatCount);
        selectedGSIndices.Clear();  // 避免重复添加旧索引

        for (int i = 0; i < splatCount; i++)
        {
            Vector3 worldPoint = objTransform.TransformPoint(positions[i]);
            if (boxBounds.Contains(worldPoint))
            {
                pointsSelected.Add(worldPoint);
                selectedGSIndices.Add(i);
            }
        }

        return pointsSelected;
    }


    void CreateOrResetSelectionSphere()
    {
        if (selectionBox != null) Destroy(selectionBox);
        selectionBox = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        selectionBox.transform.localScale = boxSize;
        selectionBox.transform.position = Camera.main.transform.position;
        selectionBox.transform.rotation = Quaternion.identity;
        Collider col = selectionBox.GetComponent<Collider>(); if (col != null) col.enabled = false;
        Renderer renderer = selectionBox.GetComponent<MeshRenderer>();
        Material mat = boxMaterial != null ? new Material(boxMaterial) : new Material(Shader.Find("Standard"));
        if (boxMaterial == null) { mat.SetFloat("_Mode", 3); mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); mat.SetInt("_ZWrite", 0); mat.DisableKeyword("_ALPHATEST_ON"); mat.EnableKeyword("_ALPHABLEND_ON"); mat.DisableKeyword("_ALPHAPREMULTIPLY_ON"); mat.renderQueue = 3000; }
        mat.color = new Color(0, 1, 1, 0.3f); renderer.material = mat;
        Debug.Log("Selection Sphere created/reset.");
    }

}