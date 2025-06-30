using System.Collections;
using Meta.XR;
using Meta.XR.EnvironmentDepth;
using UnityEngine;


public class OcclusionHandler : MonoBehaviour
{
    [SerializeField] private OVRManager ovrManager;
    [SerializeField] private EnvironmentDepthManager environmentDepthManager;
    [SerializeField] private EnvironmentRaycastManager environmentRaycastManager;

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

        while (!environmentDepthManager.IsDepthAvailable)
            yield return null;
    }
}