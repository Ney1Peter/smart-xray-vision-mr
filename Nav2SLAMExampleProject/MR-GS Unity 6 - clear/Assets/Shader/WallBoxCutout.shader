Shader "Custom/WallBoxCutout"
{
    Properties
    {
        _Color ("Color", Color) = (1,0,0,1)
        _CutCenterR ("Cut Center (xyz) + Radius (w)", Vector) = (0,0,0,0.5)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }
        Cull Off
        ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float4 _CutCenterR; // xyz = center, w = radius

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 pos : SV_POSITION; float3 wPos : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos  = UnityObjectToClipPos(v.vertex);
                o.wPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = distance(i.wPos, _CutCenterR.xyz);
                if (d < _CutCenterR.w) discard;   // ¿ª¶´
                return _Color;
            }
            ENDCG
        }
    }
}
