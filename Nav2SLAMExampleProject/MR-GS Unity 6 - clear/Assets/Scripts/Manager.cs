using UnityEngine;
using GaussianSplatting.Runtime;
using System.Reflection;

public class XRayToggle : MonoBehaviour
{
    public GaussianSplatRenderer gsRenderer;
    public float xrayOpacity = 0.1f;
    public OVRInput.Button hotKey = OVRInput.Button.One;

    static FieldInfo opacityField;          // 反射缓存
    bool xrayOn = true;

    void Awake()
    {
        // ① 抓住私有字段
        if (opacityField == null)
            opacityField = typeof(GaussianSplatRenderer)
               .GetField("m_OpacityScale",
                         BindingFlags.Instance |
                         BindingFlags.NonPublic |
                         BindingFlags.Public);

        if (opacityField == null)
        {
            Debug.LogError("❌ 在 GaussianSplatRenderer 里找不到 m_OpacityScale！");
            enabled = false;
            return;
        }

        SetOpacity(xrayOpacity);            // 默认 X-Ray 开
    }

    void Update()
    {
        if (OVRInput.GetDown(hotKey))
        {
            xrayOn = !xrayOn;
            SetOpacity(xrayOn ? xrayOpacity : 0f);
        }
    }

    /* ② 通过反射写值，再刷新材质参数 */
    void SetOpacity(float v)
    {
        opacityField.SetValue(gsRenderer, v);

        // ► 通知渲染器把新值推给 GPU
        //   大多数 fork 有这个函数；若没有就删掉。
        var m = typeof(GaussianSplatRenderer)
                .GetMethod("UpdateMaterialProperties",
                           BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (m != null) m.Invoke(gsRenderer, null);
    }
}
