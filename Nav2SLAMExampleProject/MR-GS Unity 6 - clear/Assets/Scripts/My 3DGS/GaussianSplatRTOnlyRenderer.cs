using UnityEngine;
using UnityEngine.Rendering;
using GaussianSplatting.Runtime;
using System.Reflection;

[RequireComponent(typeof(GaussianSplatRenderer))]
public class GaussianSplatRTOnlyRenderer : MonoBehaviour
{
    [Header("Render Settings")]
    public Shader splatShader;           // 这里用新的Shader（采样颜色贴图）
    public int textureSize = 512;        // 输出 RT 的分辨率

    [Header("Debug Preview")]
    public bool showOnGUI = true;        // 是否在左上角显示RT预览

    public RenderTexture outputRT;       // 生成的RT

    private GaussianSplatRenderer _gsRenderer;
    private GraphicsBuffer _posBuffer;
    private Material _material;
    private CommandBuffer _cmd;

    void Start()
    {
        // 1) 拿到同物体上的 GaussianSplatRenderer
        _gsRenderer = GetComponent<GaussianSplatRenderer>();

        // 2) 让原先系统不再渲染
        if (Application.isPlaying)
        {
            GaussianSplatRenderSystem.instance.UnregisterSplat(_gsRenderer);
            Debug.Log("[GSRT] 已关闭原自定义渲染");
        }

        if (!splatShader)
        {
            Debug.LogError("[GSRT] 未设置 splatShader");
            return;
        }
        if (!_gsRenderer.HasValidAsset)
        {
            Debug.LogError("[GSRT] GaussianSplatRenderer Asset 无效");
            return;
        }
        int splatCount = _gsRenderer.splatCount;
        if (splatCount <= 0)
        {
            Debug.LogWarning("[GSRT] splatCount = 0，无法绘制");
            return;
        }

        // 3) 获取 GPU 位置信息
        _posBuffer = _gsRenderer.GetGpuPosData();
        if (_posBuffer == null)
        {
            Debug.LogError("[GSRT] 无法获取 PosData");
            return;
        }

        // 4) 通过反射拿到真实颜色贴图 (Texture2D)
        var field = typeof(GaussianSplatRenderer).GetField(
            "m_GpuColorData",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        if (field == null)
        {
            Debug.LogError("[GSRT] 无法找到 m_GpuColorData 字段");
            return;
        }
        var colorTex = field.GetValue(_gsRenderer) as Texture;
        if (colorTex == null)
        {
            Debug.LogError("[GSRT] m_GpuColorData 不是 Texture2D 或为空");
            return;
        }

        // 5) 创建材质 & RT
        _material = new Material(splatShader);
        _material.SetBuffer("_SplatPos", _posBuffer);
        // 传入颜色贴图
        _material.SetTexture("_SplatColorTex", colorTex);
        // 告诉Shader贴图尺寸
        _material.SetFloat("_TexWidth", colorTex.width);
        _material.SetFloat("_TexHeight", colorTex.height);

        outputRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGBFloat)
        {
            name = "GS_SplatRT",
            enableRandomWrite = false,
            useMipMap = false,
            autoGenerateMips = false
        };
        outputRT.Create();

        // 6) CommandBuffer 执行
        _cmd = new CommandBuffer { name = "GSRT_Draw" };
        _cmd.SetRenderTarget(outputRT);
        _cmd.ClearRenderTarget(true, true, Color.clear);
        // TriangleStrip (枚举3) ，4个顶点 -> instanceCount = splatCount
        _cmd.DrawProcedural(Matrix4x4.identity, _material, 0, (MeshTopology)3, 4, splatCount);

        Graphics.ExecuteCommandBuffer(_cmd);

        Debug.Log($"[GSRT] Done rendering {splatCount} splats with real colorTex = {colorTex.width}x{colorTex.height}");
    }

    void OnGUI()
    {
        if (showOnGUI && outputRT != null)
        {
            GUI.Box(new Rect(10, 10, 260, 260), "GS RT Preview");
            GUI.DrawTexture(new Rect(15, 35, 256, 256), outputRT, ScaleMode.ScaleToFit, false);
        }
    }

    void OnDestroy()
    {
        _posBuffer?.Release();
        outputRT?.Release();
        _cmd?.Release();
    }
}
