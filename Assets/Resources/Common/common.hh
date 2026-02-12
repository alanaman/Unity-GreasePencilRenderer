inline float noise_randomValue (float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
}

inline float noise_interpolate (float a, float b, float t)
{
    return (1.0 - t) * a + (t * b);
}

inline float valueNoise (float2 uv)
{
    float2 i = floor(uv);
    float2 f = frac(uv);
    f = f * f * (3.0 - 2.0 * f); // Smoothstep interpolation

    uv = abs(frac(uv) - 0.5);
    float2 check = step(float2(0.5, 0.5), uv);

    float s0 = noise_randomValue(i);
    float s1 = noise_randomValue(i + float2(1.0, 0.0));
    float s2 = noise_randomValue(i + float2(0.0, 1.0));
    float s3 = noise_randomValue(i + float2(1.0, 1.0));

    float r0 = noise_interpolate(s0, s1, f.x);
    float r1 = noise_interpolate(s2, s3, f.x);
    float r = noise_interpolate(r0, r1, f.y);

    return r;
}

float SimpleNoise_float(float2 UV, float Scale)
{
    float t = 0.0;
    for(int j = 0; j < 3; j++)
    {
        // Rescale and stack layers
        t += valueNoise(UV * Scale);
        Scale *= 2.0;
        t *= 0.5;
    }
    return t;
}