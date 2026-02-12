#ifndef BLENDER_NOISE_INCLUDED
#define BLENDER_NOISE_INCLUDED

// =========================================================================
// DEPENDENCIES: PLACEHOLDER NOISE FUNCTIONS
// Use a library like Keijiro's NoiseShader or Unity.Mathematics to fill these.
// =========================================================================

// Placeholder: Standard Perlin Noise ("noise")
float RawPerlinNoise(float p) { return frac(sin(p) * 43758.5453); } // Dummy implementation
float RawPerlinNoise(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
float RawPerlinNoise(float3 p) { return RawPerlinNoise(p.xy + p.z); }
float RawPerlinNoise(float4 p) { return RawPerlinNoise(p.xyz + p.w); }

// Placeholder: Simplex Noise ("snoise")
float RawSimplexNoise(float p) { return RawPerlinNoise(p); } // Dummy implementation
float RawSimplexNoise(float2 p) { return RawPerlinNoise(p); }
float RawSimplexNoise(float3 p) { return RawPerlinNoise(p); }
float RawSimplexNoise(float4 p) { return RawPerlinNoise(p); }


float safe_noise(float co)
{
    float precision_correction = 0.5 * (abs(co) >= 1000000.0 ? 1.0 : 0.0);
    // Repeat Perlin noise texture every 100000.0 on each axis
    float p = fmod(co, 100000.0) + precision_correction;

    return RawPerlinNoise(p);
}

float safe_noise(float2 co)
{
    float2 precision_correction = 0.5 * float2((abs(co.x) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.y) >= 1000000.0 ? 1.0 : 0.0));
    float2 p = fmod(co, 100000.0) + precision_correction;

    return RawPerlinNoise(p);
}

float safe_noise(float3 co)
{
    float3 precision_correction = 0.5 * float3((abs(co.x) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.y) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.z) >= 1000000.0 ? 1.0 : 0.0));
    float3 p = fmod(co, 100000.0) + precision_correction;

    return RawPerlinNoise(p);
}

float safe_noise(float4 co)
{
    float4 precision_correction = 0.5 * float4((abs(co.x) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.y) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.z) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.w) >= 1000000.0 ? 1.0 : 0.0));
    float4 p = fmod(co, 100000.0) + precision_correction;

    // Mapping float4 input to available signatures. 
    // Logic adapted to fit standard 3D/4D noise availability.
    return RawPerlinNoise(p); 
}

// -------------------------------------------------------------------------
// Safe Simplex Noise Wrappers
// -------------------------------------------------------------------------

float safe_snoise(float co)
{
    float precision_correction = 0.5 * (abs(co) >= 1000000.0 ? 1.0 : 0.0);
    float p = fmod(co, 100000.0) + precision_correction;

    return RawSimplexNoise(p);
}

float safe_snoise(float2 co)
{
    float2 precision_correction = 0.5 * float2((abs(co.x) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.y) >= 1000000.0 ? 1.0 : 0.0));
    float2 p = fmod(co, 100000.0) + precision_correction;

    return RawSimplexNoise(p);
}

float safe_snoise(float3 co)
{
    float3 precision_correction = 0.5 * float3((abs(co.x) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.y) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.z) >= 1000000.0 ? 1.0 : 0.0));
    float3 p = fmod(co, 100000.0) + precision_correction;

    return RawSimplexNoise(p);
}

float safe_snoise(float4 co)
{
    float4 precision_correction = 0.5 * float4((abs(co.x) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.y) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.z) >= 1000000.0 ? 1.0 : 0.0),
                                               (abs(co.w) >= 1000000.0 ? 1.0 : 0.0));
    float4 p = fmod(co, 100000.0) + precision_correction;

    return RawSimplexNoise(p);
}

// -------------------------------------------------------------------------
// Noise Macros
// -------------------------------------------------------------------------

#define NOISE_FBM(T) \
  float noise_fbm(T co, float detail, float roughness, float lacunarity, int use_normalize) \
  { \
    T p = co; \
    float fscale = 1.0; \
    float amp = 1.0; \
    float maxamp = 0.0; \
    float sum = 0.0; \
    \
    for (int i = 0; i <= int(detail); i++) { \
      float t = safe_snoise(fscale * p); \
      sum += t * amp; \
      maxamp += amp; \
      amp *= roughness; \
      fscale *= lacunarity; \
    } \
    float rmd = detail - floor(detail); \
    if (rmd != 0.0) { \
      float t = safe_snoise(fscale * p); \
      float sum2 = sum + t * amp; \
      return use_normalize ? \
                  lerp(0.5 * sum / maxamp + 0.5, 0.5 * sum2 / (maxamp + amp) + 0.5, rmd) : \
                  lerp(sum, sum2, rmd); \
    } \
    else { \
      return use_normalize ? 0.5 * sum / maxamp + 0.5 : sum; \
    } \
  }

#define NOISE_MULTI_FRACTAL(T) \
  float noise_multi_fractal(T co, float detail, float roughness, float lacunarity) \
  { \
    T p = co; \
    float value = 1.0; \
    float pwr = 1.0; \
    \
    for (int i = 0; i <= (int)detail; i++) { \
      value *= (pwr * safe_snoise(p) + 1.0); \
      pwr *= roughness; \
      p *= lacunarity; \
    } \
    \
    float rmd = detail - floor(detail); \
    if (rmd != 0.0) { \
      value *= (rmd * pwr * safe_snoise(p) + 1.0); \
    } \
    \
    return value; \
  }

#define NOISE_HETERO_TERRAIN(T) \
  float noise_hetero_terrain(T co, float detail, float roughness, float lacunarity, float offset) \
  { \
    T p = co; \
    float pwr = roughness; \
    \
    /* first unscaled octave of function; later octaves are scaled */ \
    float value = offset + safe_snoise(p); \
    p *= lacunarity; \
    \
    for (int i = 1; i <= (int)detail; i++) { \
      float increment = (safe_snoise(p) + offset) * pwr * value; \
      value += increment; \
      pwr *= roughness; \
      p *= lacunarity; \
    } \
    \
    float rmd = detail - floor(detail); \
    if (rmd != 0.0) { \
      float increment = (safe_snoise(p) + offset) * pwr * value; \
      value += rmd * increment; \
    } \
    \
    return value; \
  }

#define NOISE_HYBRID_MULTI_FRACTAL(T) \
  float noise_hybrid_multi_fractal( \
      T co, float detail, float roughness, float lacunarity, float offset, float gain) \
  { \
    T p = co; \
    float pwr = 1.0; \
    float value = 0.0; \
    float weight = 1.0; \
    \
    for (int i = 0; (weight > 0.001) && (i <= (int)detail); i++) { \
      if (weight > 1.0) { \
        weight = 1.0; \
      } \
      \
      float signal = (safe_snoise(p) + offset) * pwr; \
      pwr *= roughness; \
      value += weight * signal; \
      weight *= gain * signal; \
      p *= lacunarity; \
    } \
    \
    float rmd = detail - floor(detail); \
    if ((rmd != 0.0) && (weight > 0.001)) { \
      if (weight > 1.0) { \
        weight = 1.0; \
      } \
      float signal = (safe_snoise(p) + offset) * pwr; \
      value += rmd * weight * signal; \
    } \
    \
    return value; \
  }

#define NOISE_RIDGED_MULTI_FRACTAL(T) \
  float noise_ridged_multi_fractal( \
      T co, float detail, float roughness, float lacunarity, float offset, float gain) \
  { \
    T p = co; \
    float pwr = roughness; \
    \
    float signal = offset - abs(safe_snoise(p)); \
    signal *= signal; \
    float value = signal; \
    float weight = 1.0; \
    \
    for (int i = 1; i <= (int)detail; i++) { \
      p *= lacunarity; \
      weight = clamp(signal * gain, 0.0, 1.0); \
      signal = offset - abs(safe_snoise(p)); \
      signal *= signal; \
      signal *= weight; \
      value += signal * pwr; \
      pwr *= roughness; \
    } \
    \
    return value; \
  }

/* Noise fBM. */
NOISE_FBM(float)
NOISE_FBM(float2)
NOISE_FBM(float3)
NOISE_FBM(float4)

/* Noise Multi-fractal. */
NOISE_MULTI_FRACTAL(float)
NOISE_MULTI_FRACTAL(float2)
NOISE_MULTI_FRACTAL(float3)
NOISE_MULTI_FRACTAL(float4)

/* Noise Hetero Terrain. */
NOISE_HETERO_TERRAIN(float)
NOISE_HETERO_TERRAIN(float2)
NOISE_HETERO_TERRAIN(float3)
NOISE_HETERO_TERRAIN(float4)

/* Noise Hybrid Multi-fractal. */
NOISE_HYBRID_MULTI_FRACTAL(float)
NOISE_HYBRID_MULTI_FRACTAL(float2)
NOISE_HYBRID_MULTI_FRACTAL(float3)
NOISE_HYBRID_MULTI_FRACTAL(float4)

/* Noise Ridged Multi-fractal. */
NOISE_RIDGED_MULTI_FRACTAL(float)
NOISE_RIDGED_MULTI_FRACTAL(float2)
NOISE_RIDGED_MULTI_FRACTAL(float3)
NOISE_RIDGED_MULTI_FRACTAL(float4)

#endif // BLENDER_NOISE_INCLUDED