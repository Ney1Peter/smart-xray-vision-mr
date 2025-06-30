using System.Collections;
using Appletea.Dev.PointCloud;
using Meta.XR;
using Meta.XR.EnvironmentDepth;
using UnityEngine;


public class DepthHandler : MonoBehaviour
{
    [SerializeField] private OVRManager ovrManager;
    [SerializeField] private EnvironmentDepthManager environmentDepthManager;
    [SerializeField] private EnvironmentRaycastManager environmentRaycastManager;
    [SerializeField] private PointScanner pointScanner;
    [SerializeField] private PointCloudRenderer pointCloudRenderer;

    private void Start()
    {
        StartCoroutine(EnableOcclusion());
    }

    private IEnumerator EnableOcclusion()
    {
        ovrManager.isInsightPassthroughEnabled = true;

        if (!EnvironmentDepthManager.IsSupported)
            yield break;

        environmentDepthManager.enabled = true;
        environmentDepthManager.OcclusionShadersMode = OcclusionShadersMode.SoftOcclusion;
        environmentRaycastManager.enabled = true;

        environmentDepthManager.RemoveHands = true;
        pointScanner.enabled = true;
        pointCloudRenderer.enabled = true;


        while (!environmentDepthManager.IsDepthAvailable)
            yield return null;
    }
}