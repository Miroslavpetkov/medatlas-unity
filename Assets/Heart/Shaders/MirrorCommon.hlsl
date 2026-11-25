// Shared lighting and finish for the mirrored web scenes (SceneMirror sets the globals every frame).
#ifndef MEDATLAS_MIRROR_COMMON
#define MEDATLAS_MIRROR_COMMON

// Lights of the web viewer (directions point towards the light, Unity axes; colours are linear × intensity).
float4 _MirKeyDir, _MirKeyCol;
float4 _MirFillDir, _MirFillCol;
float4 _MirRimDir, _MirRimCol;
float4 _MirCamCol;      // the small light that travels with the camera
float4 _MirSky, _MirGround;   // hemisphere light
float4 _MirEnv;         // x: environment intensity, y: exposure, z: rim (scattering) strength
float4 _MirFogCol;      // rgb: fog colour (linear), a: density per world unit (FogExp2)

#define MIR_PI 3.14159265

float MirD(float ndh, float a)
{
    float a2 = a * a;
    float d = ndh * ndh * (a2 - 1.0) + 1.0;
    return a2 / (MIR_PI * d * d + 1e-7);
}

float MirV(float ndl, float ndv, float a)
{
    float a2 = a * a;
    float gv = ndl * sqrt(a2 + (1.0 - a2) * ndv * ndv);
    float gl = ndv * sqrt(a2 + (1.0 - a2) * ndl * ndl);
    return 0.5 / max(gv + gl, 1e-6);
}

float3 MirF(float3 f0, float vdh)
{
    float k = pow(1.0 - vdh, 5.0);
    return f0 + (1.0 - f0) * k;
}

// One directional light on a three.js MeshStandardMaterial (+ an optional clear coat lobe).
float3 MirDirect(float3 l, float3 c, float3 n, float3 v, float3 diff, float3 f0, float a, float coat)
{
    float ndl = saturate(dot(n, l));
    if (ndl <= 0.0) return 0;
    float3 h = normalize(l + v);
    float ndv = saturate(dot(n, v)) + 1e-4;
    float ndh = saturate(dot(n, h));
    float vdh = saturate(dot(v, h));
    float3 spec = MirF(f0, vdh) * MirV(ndl, ndv, a) * MirD(ndh, a);
    float3 r = diff / MIR_PI + spec;
    if (coat > 0.0)
    {
        const float ca = 0.32 * 0.32;
        r = r * (1.0 - coat * MirF(0.04, vdh).x) + coat * MirF(0.04, vdh) * MirV(ndl, ndv, ca) * MirD(ndh, ca);
    }
    return c * ndl * r;
}

// A soft studio around the model (the web uses three.js RoomEnvironment): brighter above, darker below.
float3 MirEnvRadiance(float3 d)
{
    float up = saturate(d.y * 0.5 + 0.5);
    return lerp(float3(0.18, 0.18, 0.2), float3(1.05, 1.05, 1.1), up * up);
}

// Environment specular without a prefiltered map (Karis' analytic approximation of the split sum).
float3 MirEnvBRDF(float3 f0, float rough, float ndv)
{
    const float4 c0 = float4(-1, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1, 0.0425, 1.04, -0.04);
    float4 r = rough * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * ndv)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
    return f0 * ab.x + ab.y;
}

float3 MirrorShade(float3 albedo, float3 n, float3 v, float rough, float metal, float coat)
{
    rough = clamp(rough, 0.04, 1.0);
    metal = saturate(metal);
    float3 diff = albedo * (1.0 - metal);
    float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metal);
    float a = rough * rough;
    coat *= (1.0 - metal);
    float3 c = 0;
    c += MirDirect(_MirKeyDir.xyz, _MirKeyCol.rgb, n, v, diff, f0, a, coat);
    c += MirDirect(_MirFillDir.xyz, _MirFillCol.rgb, n, v, diff, f0, a, coat);
    c += MirDirect(_MirRimDir.xyz, _MirRimCol.rgb, n, v, diff, f0, a, coat);
    c += MirDirect(v, _MirCamCol.rgb, n, v, diff, f0, a, coat);
    // hemisphere light (three.js: irradiance × Lambert)
    float3 hemi = lerp(_MirGround.rgb, _MirSky.rgb, dot(n, float3(0, 1, 0)) * 0.5 + 0.5);
    c += hemi * diff / MIR_PI;
    // environment: diffuse irradiance and a rough-aware reflection
    float ndv = saturate(dot(n, v));
    float3 r = reflect(-v, n);
    c += _MirEnv.x * (diff * MirEnvRadiance(n) * 0.85 + MirEnvBRDF(f0, rough, ndv) * MirEnvRadiance(r) * lerp(1.0, 0.35, rough));
    // light scattered under the surface of living tissue shows as a soft warm glow at grazing angles
    float rim = pow(1.0 - ndv, 3.0) * _MirEnv.z;
    c += rim * albedo * (0.6 + 0.4 * _MirKeyCol.rgb / max(max(_MirKeyCol.r, _MirKeyCol.g), 1e-3));
    return c;
}

// three.js ACESFilmicToneMapping (r169) with the viewer's exposure, then fog (FogExp2) like the web scene.
float3 MirACES(float3 c)
{
    c *= _MirEnv.y / 0.6;
    float3 i = float3(dot(float3(0.59719, 0.35458, 0.04823), c), dot(float3(0.07600, 0.90834, 0.01566), c), dot(float3(0.02840, 0.13383, 0.83777), c));
    float3 a = i * (i + 0.0245786) - 0.000090537;
    float3 b = i * (0.983729 * i + 0.4329510) + 0.238081;
    i = a / b;
    float3 o = float3(dot(float3(1.60475, -0.53108, -0.07367), i), dot(float3(-0.10208, 1.10813, -0.00605), i), dot(float3(-0.00327, -0.07276, 1.07602), i));
    return saturate(o);
}

float3 MirrorFinish(float3 c, float3 posWS, float toneMap)
{
    if (toneMap > 0.5) c = MirACES(c);
    float depth = -TransformWorldToView(posWS).z;
    float f = 1.0 - exp(-_MirFogCol.a * _MirFogCol.a * depth * depth);
    return lerp(c, _MirFogCol.rgb, saturate(f));
}

// Live section (the 3D tools' "Срез"): keep what is in front of the plane, dot(n, p) + w >= 0 (world space).
float4 _MirClip;
float _MirClipOn;
void MirClipTest(float3 ws)
{
    if (_MirClipOn > 0.5 && dot(_MirClip.xyz, ws) + _MirClip.w < 0.0) discard;
}

#endif
