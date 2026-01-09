#pragma once

#define gpMaterialFlag uint
static const uint GP_FLAG_NONE = 0u;
static const uint GP_STROKE_ALIGNMENT_STROKE = 1u;
static const uint GP_STROKE_ALIGNMENT_OBJECT = 2u;
static const uint GP_STROKE_ALIGNMENT_FIXED = 3u;
static const uint GP_STROKE_ALIGNMENT = 0x3u;
static const uint GP_STROKE_OVERLAP = (1u << 2u);
static const uint GP_STROKE_TEXTURE_USE = (1u << 3u);
static const uint GP_STROKE_TEXTURE_STENCIL = (1u << 4u);
static const uint GP_STROKE_TEXTURE_PREMUL = (1u << 5u);
static const uint GP_STROKE_DOTS = (1u << 6u);
static const uint GP_STROKE_HOLDOUT = (1u << 7u);
static const uint GP_FILL_HOLDOUT = (1u << 8u);
static const uint GP_FILL_TEXTURE_USE = (1u << 10u);
static const uint GP_FILL_TEXTURE_PREMUL = (1u << 11u);
static const uint GP_FILL_TEXTURE_CLIP = (1u << 12u);
static const uint GP_FILL_GRADIENT_USE = (1u << 13u);
static const uint GP_FILL_GRADIENT_RADIAL = (1u << 14u);
static const uint GP_FILL_FLAGS = (GP_FILL_TEXTURE_USE | GP_FILL_TEXTURE_PREMUL | GP_FILL_TEXTURE_CLIP | GP_FILL_GRADIENT_USE | GP_FILL_GRADIENT_RADIAL | GP_FILL_HOLDOUT);

#define gpLightType uint
static const uint GP_LIGHT_TYPE_POINT = 0u;
static const uint GP_LIGHT_TYPE_SPOT = 1u;
static const uint GP_LIGHT_TYPE_SUN = 2u;
static const uint GP_LIGHT_TYPE_AMBIENT = 3u;


#define GP_IS_STROKE_VERTEX_BIT (1 << 30)
#define GP_VERTEX_ID_SHIFT 2


struct gpMaterial {
  float4 stroke_color;
  float4 fill_color;
  float4 fill_mix_color;
  float4 fill_uv_rot_scale;
#ifndef GPU_SHADER
  float2 fill_uv_offset;
  float2 alignment_rot;
  float stroke_texture_mix;
  float stroke_u_scale;
  float fill_texture_mix;
  gpMaterialFlag flag;
#else
  /* Some drivers are completely messing the alignment or the fetches here.
   * We are forced to pack these into float4 otherwise we only get 0.0 as value. */
  /* NOTE(@fclem): This was the case on MacOS OpenGL implementation.
   * This might be fixed in newer APIs. */
  float4 packed1;
  float4 packed2;
#  define _fill_uv_offset packed1.xy
#  define _alignment_rot packed1.zw
#  define _stroke_texture_mix packed2.x
#  define _stroke_u_scale packed2.y
#  define _fill_texture_mix packed2.z
  /** NOTE(@fclem): Needs floatBitsToUint(). */
#  define _flag packed2.w
#endif
};

#ifdef GP_LIGHT
struct gpLight {
#  ifndef GPU_SHADER
  float3 color;
  gpLightType type;
  float3 right;
  float spot_size;
  float3 up;
  float spot_blend;
  float3 forward;
  float _pad0;
  float3 position;
  float _pad1;
#  else
  /* Some drivers are completely messing the alignment or the fetches here.
   * We are forced to pack these into float4 otherwise we only get 0.0 as value. */
  /* NOTE(@fclem): This was the case on MacOS OpenGL implementation.
   * This might be fixed in newer APIs. */
  float4 packed0;
  float4 packed1;
  float4 packed2;
  float4 packed3;
  float4 packed4;
#    define _color packed0.xyz
#    define _type packed0.w
#    define _right packed1.xyz
#    define _spot_size packed1.w
#    define _up packed2.xyz
#    define _spot_blend packed2.w
#    define _forward packed3.xyz
#    define _position packed4.xyz
#  endif
};
BLI_STATIC_ASSERT_ALIGN(gpLight, 16)
#endif
