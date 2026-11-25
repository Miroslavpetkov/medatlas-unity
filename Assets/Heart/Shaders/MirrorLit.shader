// Surfaces of the mirrored web scenes (SceneMirror.cs). Lighting follows the web viewer (src/core/viewer.ts):
// hemisphere light, key / fill / rim lights, a light at the camera and a soft studio environment, with the
// same physically based terms as three.js MeshStandardMaterial and its ACES filmic tone mapping (exposure 1.15),
// so a scene looks the same in both versions. A thin wet clear coat and a soft rim (light scattered through
// tissue) add the realistic finish. No shader keywords besides instancing: render state comes from properties,
// so nothing can be stripped from the WebGL build.
Shader "MedAtlas/MirrorLit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Emission ("Emission", Color) = (0, 0, 0, 1)
        _Opacity ("Opacity", Float) = 1
        _Rough ("Roughness", Float) = 0.55
        _Metal ("Metalness", Float) = 0.05
        _Unlit ("Unlit (basic / line material)", Float) = 0
        _VCol ("Use vertex colours", Float) = 0
        _UseInst ("Use instance colours", Float) = 0
        _Coat ("Wet clear coat", Float) = 0.3
        _ToneMap ("Tone mapped", Float) = 1
        _SrcBlend ("Src Blend", Float) = 1
        _DstBlend ("Dst Blend", Float) = 0
        _ZWrite ("Z Write", Float) = 1
        _ZTest ("Z Test", Float) = 4
        _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "MirrorLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MirrorCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Emission;
                half _Opacity;
                half _Rough;
                half _Metal;
                half _Unlit;
                half _VCol;
                half _UseInst;
                half _Coat;
                half _ToneMap;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                half4 c = half4(1, 1, 1, 1);
                if (_VCol > 0.5h) c = v.color;
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                if (_UseInst > 0.5h) c.rgb *= UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor).rgb;
                #endif
                o.color = c;
                return o;
            }

            half4 frag (Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo = _Color.rgb * i.color.rgb;
                half3 col;
                if (_Unlit > 0.5h) col = albedo;
                else
                {
                    float3 n = normalize(i.normalWS);
                    n = IS_FRONT_VFACE(face, true, false) ? n : -n;   // double-sided surfaces are lit from the side that faces the camera (as in three.js)
                    float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                    col = MirrorShade(albedo, n, v, _Rough, _Metal, _Coat);
                }
                col += _Emission.rgb;
                col = MirrorFinish(col, i.positionWS, _ToneMap);
                return half4(col, _Opacity * i.color.a);
            }
            ENDHLSL
        }
    }
}
