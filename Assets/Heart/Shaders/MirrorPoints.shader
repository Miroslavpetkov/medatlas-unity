// Particles of the mirrored web scenes (three.js Points). Each point is a quad (4 vertices at the point, the uv says
// which corner) turned to the camera here. The web draws flat squares; here each particle is a small lit bead.
Shader "MedAtlas/MirrorPoints"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Float) = 1
        _PtSize ("Point diameter (world)", Float) = 0.02
        _PtPix ("Point diameter (pixels, 0 = world size)", Float) = 0
        _VCol ("Use vertex colours", Float) = 0
        _ToneMap ("Tone mapped", Float) = 1
        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
        _ZWrite ("Z Write", Float) = 0
        _ZTest ("Z Test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Pass
        {
            Name "MirrorPoints"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MirrorCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Opacity;
                float _PtSize;
                float _PtPix;
                half _VCol;
                half _ToneMap;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 c : TEXCOORD0; float3 positionWS : TEXCOORD1; half4 color : COLOR; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 w = TransformObjectToWorld(v.positionOS.xyz);
                float3 vc = TransformWorldToView(w);
                if (_PtPix > 0)
                {
                    o.positionCS = TransformWViewToHClip(vc);
                    o.positionCS.xy += v.uv * _PtPix / _ScreenParams.xy * o.positionCS.w;
                }
                else
                {
                    vc.xy += v.uv * _PtSize * 0.5;
                    o.positionCS = TransformWViewToHClip(vc);
                }
                o.c = v.uv;
                o.positionWS = w;
                o.color = _VCol > 0.5h ? v.color : half4(1, 1, 1, 1);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float r2 = dot(i.c, i.c);
                clip(1.0 - r2);
                float z = sqrt(saturate(1.0 - r2));
                // a bead lit from the key light's side, with a small highlight
                float3 n = normalize(float3(i.c.x, i.c.y, z));
                float3 l = normalize(float3(0.45, 0.6, 0.65));
                float3 base = _Color.rgb * i.color.rgb;
                float3 c = base * (0.62 + 0.55 * saturate(dot(n, l))) + pow(saturate(dot(reflect(-l, n), float3(0, 0, 1))), 24.0) * 0.35;
                c = MirrorFinish(c, i.positionWS, _ToneMap);
                float edge = 1.0 - smoothstep(0.82, 1.0, r2);
                return half4(c, _Opacity * i.color.a * edge);
            }
            ENDHLSL
        }
    }
}
