// Labels of the mirrored web scenes (three.js Sprite with a text texture): a quad turned to the camera, sized by
// the object's scale (SceneMirror grows it so the text stays readable, like the web version).
Shader "MedAtlas/MirrorSprite"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Float) = 1
        _Center ("Center (uv)", Vector) = (0.5, 0.5, 0, 0)
        _Rotation ("Rotation", Float) = 0
        _ToneMap ("Tone mapped", Float) = 1
        _ZTest ("Z Test", Float) = 4
        _ZWrite ("Z Write", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Pass
        {
            Name "MirrorSprite"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MirrorCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Opacity;
                float4 _Center;
                float _Rotation;
                half _ToneMap;
                float _ZTest;
                float _ZWrite;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float4x4 m = GetObjectToWorldMatrix();
                float3 w = float3(m._m03, m._m13, m._m23);
                float sx = length(float3(m._m00, m._m10, m._m20));
                float sy = length(float3(m._m01, m._m11, m._m21));
                float3 vc = TransformWorldToView(w);
                float2 a = (v.uv - _Center.xy) * float2(sx, sy);
                float cs = cos(_Rotation), sn = sin(_Rotation);
                vc.xy += float2(cs * a.x - sn * a.y, sn * a.x + cs * a.y);
                o.positionCS = TransformWViewToHClip(vc);
                o.uv = v.uv;
                o.positionWS = w;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                MirClipTest(i.positionWS);
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float3 c = t.rgb * _Color.rgb;
                if (_ToneMap > 0.5h) c = MirACES(c);
                return half4(c, t.a * _Opacity);
            }
            ENDHLSL
        }
    }
}
