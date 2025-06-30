Shader"GaussianSplatting/SimpleRTPass"
{
    Properties
    {
        _TexWidth("TexWidth", Float) = 1024
        _TexHeight("TexHeight", Float) = 1024
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
ZWrite Off

ZTest Always

Cull Off

Blend One
One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
#include "UnityCG.cginc"

StructuredBuffer<float4> _SplatPos; // xyz=pos, w=scale
sampler2D _SplatColorTex;

float _TexWidth;
float _TexHeight;

struct v2f
{
    float4 vertex : SV_POSITION;
    float2 uv : TEXCOORD0;
};

v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
{
    v2f o;
    float4 splat = _SplatPos[instanceID];
    float scale = splat.w;

                // 构建quad
    float2 quad[4] =
    {
        float2(-0.5, -0.5),
                    float2(0.5, -0.5),
                    float2(-0.5, 0.5),
                    float2(0.5, 0.5)
    };
    float2 offset = quad[vertexID % 4] * scale;
    float3 worldPos = splat.xyz + float3(offset, 0);
    o.vertex = UnityObjectToClipPos(float4(worldPos, 1.0));

                // 根据instanceID 计算颜色贴图坐标
    float i = instanceID;
    float w = _TexWidth;
    float h = _TexHeight;
                // 让它一行 w 个像素
    float2 uv;
    uv.x = (fmod(i, w) + 0.5) / w;
    uv.y = (floor(i / w) + 0.5) / h;
    o.uv = uv;

    return o;
}

float4 frag(v2f i) : SV_Target
{
                // 采样贴图
    float4 c = tex2D(_SplatColorTex, i.uv);
    return c;
}
            ENDHLSL
        }
    }
}