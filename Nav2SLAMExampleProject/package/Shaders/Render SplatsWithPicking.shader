// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/RenderSplatsWithGlobalPicking"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            ZWrite Off
            Blend OneMinusDstAlpha One
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require compute
            #pragma use_dxc

            #include "UnityCG.cginc"
            #include "GaussianSplatting.hlsl"

            StructuredBuffer<uint> _OrderBuffer;
            StructuredBuffer<SplatViewData> _SplatViewData;
            ByteAddressBuffer _SplatSelectedBits;
            uint _SplatBitsValid;
            uint _OptimizeForQuest;

            float _GlobalPicking;

            float3 EncodeIDToColor(uint id)
            {
                return float3(
                    ((id >> 16) & 0xFF),
                    ((id >> 8) & 0xFF),
                    (id & 0xFF)
                ) / 255.0;
            }

            struct v2f
            {
                half4 col : COLOR0;
                float2 pos : TEXCOORD0;
                float4 vertex : SV_POSITION;
                uint id : TEXCOORD1;
            };

            v2f vert(uint vtxID : SV_VertexID, uint instID : SV_InstanceID)
            {
                v2f o = (v2f)0;
                instID = _OrderBuffer[instID];
                o.id = instID;

                SplatViewData view = _SplatViewData[instID];
                float4 centerClipPos = view.pos;

                if (_OptimizeForQuest != 0)
                {
                    SplatData splat = LoadSplatData(instID);
                    float3 centerWorldPos = mul(unity_ObjectToWorld, float4(splat.pos, 1)).xyz;
                    centerClipPos = mul(UNITY_MATRIX_VP, float4(centerWorldPos, 1));
                }

                if (centerClipPos.w <= 0)
                {
                    o.vertex = asfloat(0x7fc00000);
                }
                else
                {
                    o.col.r = f16tof32(view.color.x >> 16);
                    o.col.g = f16tof32(view.color.x);
                    o.col.b = f16tof32(view.color.y >> 16);
                    o.col.a = f16tof32(view.color.y);

                    float2 quadPos = float2(vtxID & 1, (vtxID >> 1) & 1) * 2.0 - 1.0;
                    quadPos *= 2;
                    o.pos = quadPos;

                    float2 deltaScreenPos = (quadPos.x * view.axis1 + quadPos.y * view.axis2) * 2 / _ScreenParams.xy;
                    o.vertex = centerClipPos;
                    o.vertex.xy += deltaScreenPos * centerClipPos.w;

                    if (_SplatBitsValid != 0)
                    {
                        uint wordIdx = instID / 32;
                        uint bitIdx = instID & 31;
                        uint selVal = _SplatSelectedBits.Load(wordIdx * 4);
                        if (selVal & (1 << bitIdx))
                        {
                            o.col.a = -1;
                        }
                    }
                }

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                if (_GlobalPicking > 0.5)
                {
                    return half4(EncodeIDToColor(i.id), 1.0);
                }

                float power = -dot(i.pos, i.pos);
                half alpha = exp(power);

                if (i.col.a >= 0)
                {
                    alpha = saturate(alpha * i.col.a);
                }
                else
                {
                    half3 selectedColor = half3(1, 0, 1);
                    if (alpha > 7.0 / 255.0)
                    {
                        if (alpha < 10.0 / 255.0)
                        {
                            alpha = 1;
                            i.col.rgb = selectedColor;
                        }
                        alpha = saturate(alpha + 0.3);
                    }
                    i.col.rgb = lerp(i.col.rgb, selectedColor, 0.5);
                }

                if (alpha < 1.0 / 255.0)
                    discard;

                return half4(i.col.rgb * alpha, alpha);
            }
            ENDCG
        }
    }
}
