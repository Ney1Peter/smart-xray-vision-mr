Shader"Unlit/SelectivePassthroughAdjustable"
{
    Properties
    {
        _MainTex("Mask Texture", 2D) = "white" {}
        _Inflation("Inflation", Float) = 0
        _InvertedAlpha("Inverted Alpha", Range(0,1)) = 1

        [Header(Transparency Control)]
        _PassthroughAlpha("Passthrough Area Alpha", Range(0,1)) = 0
        _VirtualAlpha("Virtual Area Alpha", Range(0,1)) = 1

        [Header(Depth Test)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4 //"LessEqual"
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
LOD 100

        Pass
        {
ZWrite Off

ZTest [_ZTest]

            // 关键：使用SrcAlpha, OneMinusSrcAlpha混合，实现半透明叠加
Blend SrcAlpha
OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
#include "UnityCG.cginc"

struct appdata
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
    float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    float2 uv : TEXCOORD0;
    float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
};

sampler2D _MainTex;
float4 _MainTex_ST;
float _Inflation;
float _InvertedAlpha;
float _PassthroughAlpha;
float _VirtualAlpha;

v2f vert(appdata v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_OUTPUT(v2f, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.vertex = UnityObjectToClipPos(v.vertex + v.normal * _Inflation);
    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
    return o;
}

fixed4 frag(v2f i) : SV_Target
{
    fixed4 col = tex2D(_MainTex, i.uv);

                // 原Shader使用红色通道判断窗口区域
    float mask = lerp(col.r, 1.0 - col.r, _InvertedAlpha);

                // mask=1:窗口内；mask=0:窗口外
                // 根据mask值决定使用哪种透明度
    float alpha = lerp(_VirtualAlpha, _PassthroughAlpha, mask);

                // 输出黑色遮罩，alpha决定显示VST程度
    return fixed4(0, 0, 0, alpha);
}
            ENDCG
        }
    }
}
