#pragma once

inline float2 safe_normalize(const float2 a)
{
    const float t = length(a);
    return (t != 0.0f) ? a * (1.0f / t) : a;
}

inline float3 safe_normalize(const float3 a)
{
    const float t = length(a);
    return (t != 0.0f) ? a * (1.0f / t) : a;
}

float2 safe_normalize_and_get_length(float2 vec, out float out_length)
{
    out_length = dot(vec, vec);
    const float threshold = 1e-35f;
    if (out_length > threshold) {
        out_length = sqrt(out_length);
        return vec / out_length;
    }
    /* Either the vector is small or one of its values contained `nan`. */
    out_length = 0.0f;
    float2 result = float2(0,0);
    result[0] = 1.0f;
    return result;
}


bool flag_test(uint flag, uint val)
{
    return (flag & val) != 0u;
}
bool flag_test(int flag, uint val)
{
    return flag_test(uint(flag), val);
}
bool FlagTest(uint flag, int val)
{
    return (flag & (uint)val) != 0u;
}
bool flag_test(int flag, int val)
{
    return (flag & val) != 0;
}