// Assets/DepthSampler.cs
using UnityEngine;
         // Depth API 包的命名空间

public class DepthSampler : MonoBehaviour
{
#if UNITY_ANDROID && !UNITY_EDITOR     // 只在真机编译
    private EnvironmentDepthManager edm;

    void Start()
    {
        edm = FindObjectOfType<EnvironmentDepthManager>();

        // v74+ 的正确字段名；相当于 Inspector 里勾 Eye Depth Enabled
        OVRManager.eyeDepthEnabled = true;
    }

    void Update()
    {
        // 高层 API：每帧尝试拿一张 CPU 端深度图（512×512，ushort，单位毫米）
        if (edm != null && edm.TryGetEnvironmentDepthCpuImage(out var img))
        {
            Debug.Log($"Depth  {img.Width}×{img.Height}   首像素 = {img.GetRow(0)[0]} mm");
            img.Dispose();     // 用完必须释放
        }
    }
#endif
}
