using UnityEngine;
using UnityEngine.Rendering;

public class TestSimpleSplatRender : MonoBehaviour
{
    public Shader splatShader;
    public int splatCount = 1000;
    public int textureSize = 512;

    private Material _material;
    private GraphicsBuffer _posBuffer;
    private GraphicsBuffer _colorBuffer;
    private RenderTexture _rt;
    private CommandBuffer _cmd;

    void Start()
    {
        // 创建材质
        _material = new Material(splatShader);

        // 创建 RenderTexture
        _rt = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGBFloat)
        {
            name = "TestRT",
            enableRandomWrite = false,
            useMipMap = false,
            autoGenerateMips = false
        };
        _rt.Create();

        // 创建数据
        Vector4[] posData = new Vector4[splatCount];
        Vector4[] colorData = new Vector4[splatCount];
        for (int i = 0; i < splatCount; i++)
        {
            float x = Random.Range(-1f, 1f);
            float y = Random.Range(-1f, 1f);
            float scale = 0.01f;
            posData[i] = new Vector4(x, y, 0, scale);
            colorData[i] = new Color(Random.value, Random.value, Random.value, 1);
        }

        _posBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, splatCount, sizeof(float) * 4);
        _colorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, splatCount, sizeof(float) * 4);
        _posBuffer.SetData(posData);
        _colorBuffer.SetData(colorData);

        _material.SetBuffer("_SplatPos", _posBuffer);
        _material.SetBuffer("_SplatColor", _colorBuffer);

        // 设置命令缓冲区
        _cmd = new CommandBuffer { name = "Test Splat Render" };
        _cmd.SetRenderTarget(_rt);
        _cmd.ClearRenderTarget(true, true, Color.clear);
        _cmd.DrawProcedural(
            Matrix4x4.identity,
            _material,
            0,
            (MeshTopology)3, // TriangleStrip
            4,
            splatCount);

        Graphics.ExecuteCommandBuffer(_cmd);

        Debug.Log("✅ Render done to RT");
    }

    void OnGUI()
    {
        if (_rt != null)
        {
            GUI.DrawTexture(new Rect(10, 10, 256, 256), _rt, ScaleMode.ScaleToFit, false);
        }
    }

    private void OnDestroy()
    {
        _posBuffer?.Release();
        _colorBuffer?.Release();
        _rt?.Release();
        _cmd?.Release();
    }
}
