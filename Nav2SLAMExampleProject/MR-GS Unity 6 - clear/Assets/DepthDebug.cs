// Assets/DepthSampler.cs
using UnityEngine;
         // Depth API ���������ռ�

public class DepthSampler : MonoBehaviour
{
#if UNITY_ANDROID && !UNITY_EDITOR     // ֻ���������
    private EnvironmentDepthManager edm;

    void Start()
    {
        edm = FindObjectOfType<EnvironmentDepthManager>();

        // v74+ ����ȷ�ֶ������൱�� Inspector �ﹴ Eye Depth Enabled
        OVRManager.eyeDepthEnabled = true;
    }

    void Update()
    {
        // �߲� API��ÿ֡������һ�� CPU �����ͼ��512��512��ushort����λ���ף�
        if (edm != null && edm.TryGetEnvironmentDepthCpuImage(out var img))
        {
            Debug.Log($"Depth  {img.Width}��{img.Height}   ������ = {img.GetRow(0)[0]} mm");
            img.Dispose();     // ��������ͷ�
        }
    }
#endif
}
