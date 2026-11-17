// Fresnel "glass" shell used for the see-through (internal) view of the heart walls and blood volumes.
Shader "MedAtlas/XRay"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.25, 0.25, 1)
        _RimPower ("Rim Power", Float) = 2.2
        _MinAlpha ("Min Alpha", Range(0,1)) = 0.04
        _MaxAlpha ("Max Alpha", Range(0,1)) = 0.6
        _Lit ("Diffuse amount", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "XRay"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _RimPower;
                half _MinAlpha;
                half _MaxAlpha;
                half _Lit;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half ndv = saturate(dot(n, v));
                half rim = pow(1.0h - ndv, _RimPower);
                Light l = GetMainLight();
                half diff = saturate(dot(n, l.direction)) * 0.7h + 0.3h;
                half3 col = _Color.rgb * lerp(1.0h, diff, _Lit) + rim * _Color.rgb * 0.8h;
                half a = lerp(_MinAlpha, _MaxAlpha, rim) * _Color.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
