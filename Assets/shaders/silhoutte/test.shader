Shader "Custom/UnlitProcedural"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white"
        _PyramidHeight("Pyramid Height", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            
            HLSLPROGRAM

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0
            #pragma require geometry
                        
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../greasepencil/common_shader_util.hlsl"
            // Define a struct to match the one in your C# script
            struct VertexData
            {
                float3 position;
                float3 normal;
            };

            // Declare the GraphicsBuffers that we will set from the C# script
            StructuredBuffer<int> _MeshIndices;
            StructuredBuffer<VertexData> _Vertices;

            struct v2f
            {
                float3 normalWS : TEXCOORD0; // World space normal
                float4 vertex : SV_POSITION;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            float2 project_to_screenspace(float4 v)
            {
                return ((v.xy / v.w) * 0.5f + 0.5f) * _ScreenParams.xy;
            }
            float stroke_radius_modulate(float radius)
            {
                float3x3 obj3x3 = (float3x3)unity_ObjectToWorld;
                float3 scaled = mul(obj3x3, float3(radius * 0.57735, radius * 0.57735, radius * 0.57735));
                radius = length(scaled);

                float screen_radius = radius * -(UNITY_MATRIX_P[1][1]) * _ScreenParams.y;
                
                return screen_radius;
            }
            
            float3 GetCrossing(float3 pos1, float3 pos2, float s1, float s2)
            {
                // We need to find the interpolation factor 't' such that
                // lerp(vA.scalar, vB.scalar, t) == 0
                //
                // sA * (1-t) + sB * t = 0
                // sA - sA*t + sB*t = 0
                // sA = t * (sA - sB)
                // t = sA / (sA - sB)
                
                // Avoid division by zero, though this case (sA == sB)
                // should be filtered out by the (sA * sB < 0) check.
                float t = s1 / (s1 - s2);

                // Linearly interpolate the clip-space positions
                return lerp(pos1, pos2, t);
            }
            
            bool TryGetZeroLine(float3 pos1, float3 pos2, float3 pos3, float s1, float s2, float s3, out float3 points[2])
            {
                // --- Graceful Handling ---
                // Check if all scalars are on the same side of zero.
                // If so, no line can exist, and we just return early.
                if ((s1 > 0 && s2 > 0 && s3 > 0) ||
                    (s1 < 0 && s2 < 0 && s3 < 0))
                {
                    return false; // Gracefully outputs no geometry
                }
                int points_found = 0;

                // We prioritize checking vertices that are *exactly* zero.
                // This correctly handles cases where the line passes through a vertex.
                if (s1 == 0) { points[points_found++] = pos1; }
                if (s2 == 0 && points_found < 2) { points[points_found++] = pos2; }
                if (s3 == 0 && points_found < 2) { points[points_found++] = pos3; }

                // Now, check for edge crossings *if we still need points*.
                // A crossing exists if signs are different (sA * sB < 0).
                if (points_found < 2 && s1*s2 < 0)
                {
                    points[points_found++] = GetCrossing(pos1, pos2, s1, s2);
                }
                if (points_found < 2 && s2 * s3 < 0)
                {
                    points[points_found++] = GetCrossing(pos2, pos3, s2, s3);
                }
                if (points_found < 2 && s3 * s1 < 0)
                {
                    points[points_found++] = GetCrossing(pos3, pos1, s3, s1);
                }
                if (points_found == 2)
                {
                    return true;
                }
                return false;
            }
            
            // Vertex Shader
            // We use SV_VertexID to get the index of the vertex being processed
            v2f vert (uint vertexID : SV_VertexID)
            {
                v2f o;

                // 1. Use the vertexID to find the correct index from the index buffer
                uint faceIdx = vertexID / 6;
                
                int vIdx0 = _MeshIndices[faceIdx*3+0];
                int vIdx1 = _MeshIndices[faceIdx*3+1];
                int vIdx2 = _MeshIndices[faceIdx*3+2];
                
                // 2. Use that index to get the actual vertex data (pos/normal)
                float3 p0 = _Vertices[vIdx0].position;
                float3 p1 = _Vertices[vIdx1].position;
                float3 p2 = _Vertices[vIdx2].position;
                float3 n0 = _Vertices[vIdx0].normal;
                float3 n1 = _Vertices[vIdx1].normal;
                float3 n2 = _Vertices[vIdx2].normal;

                float3 dirToCam0 = normalize(_WorldSpaceCameraPos - p0);
                float3 dirToCam1 = normalize(_WorldSpaceCameraPos - p1);
                float3 dirToCam2 = normalize(_WorldSpaceCameraPos - p2);

                float dot0 = dot(n0, dirToCam0);
                float dot1 = dot(n1, dirToCam1);
                float dot2 = dot(n2, dirToCam2);

                float3 zeroLine[2];
                if (!TryGetZeroLine(p0, p1, p2, dot0, dot1, dot2, zeroLine))
                {
                    //discard
                    o.vertex = float4(0.0f, 0.0f, -3e36f, 0.0f);
                    o.normalWS = float3(0,0,0);
                    return o;
                }
                bool isSecond = (vertexID%6 > 2);
                int vI = vertexID%6;

                float x;
                float y;

                if (!isSecond)
                {
                    x = float(vI & 1) * 2.0f - 1.0f; /* [-1..1] */
                    y = float(vI & 2) - 1.0f;        /* [-1..1] */
                }
                else
                {
                    x = -(float(vI+1 & 1) * 2.0f - 1.0f); /* [-1..1] */
                    y = -(float(vI+1 & 2) - 1.0f);        /* [-1..1] */
                }

                bool is_on_zp1 = (x == -1.0f);
                
                float3 wpos1 = TransformObjectToWorld(zeroLine[0]);
                float3 wpos2 = TransformObjectToWorld(zeroLine[1]);
                // float4 ndc_adj = TransformWorldToHClip(wpos_adj);
                float4 ndc1 = TransformWorldToHClip(wpos1.xyz);
                float4 ndc2 = TransformWorldToHClip(wpos2.xyz);

                o.vertex = (is_on_zp1) ? ndc1 : ndc2;

                float2 ss1 = project_to_screenspace(ndc1);
                float2 ss2 = project_to_screenspace(ndc2);

                float edge_len;
                float2 edge_dir = safe_normalize_and_get_length(ss2 - ss1, edge_len);

                float radius = 0.1;
                radius = stroke_radius_modulate(radius);
                float clamped_radius = max(0.0f, radius);
                
                /* Mitter tangent vector. */
                float2 miter_tan = edge_dir;
                /* Rotate 90 degrees counter-clockwise. */
                float2 miter = float2(-miter_tan.y, miter_tan.x);
                float2 screen_ofs = miter * y;
                
                float2 clip_space_per_pixel = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
                o.vertex.xy += screen_ofs * clip_space_per_pixel * clamped_radius;
                // if (isSecond)
                // {
                //     p0+=n0;
                //     p1+=n1;
                //     p2+=n2;
                // }
                // // 3. Transform the vertex position from object space to clip space
                // if (vI==0)
                // {
                //     o.vertex = TransformObjectToHClip(p0);
                //     o.normalWS = TransformObjectToWorldDir(n0);
                // }
                // else if (vI==1)
                // {
                //     o.vertex = TransformObjectToHClip(p1);
                //     o.normalWS = TransformObjectToWorldDir(n1);
                // }
                // else if (vI==2)
                // {
                //     o.vertex = TransformObjectToHClip(p2);
                //     o.normalWS = TransformObjectToWorldDir(n2);
                // }
                
                // else // (vI==3)
                // {
                //     o.vertex = TransformObjectToHClip(p2);
                //     o.normalWS = TransformObjectToWorldDir(n2);
                // }
                //get normals
                //find zero points
                
                return o;
            }

            // Fragment Shader
            // This is a simple implementation that uses the normal for basic lighting
            half4 frag (v2f i) : SV_Target
            {
                // Normalize the incoming normal vector
                float3 normal = normalize(i.normalWS);
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, float2(0,0)) * _BaseColor;
                return color;
                // // Get the main light direction from Unity's built-in variables
                // float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                //
                // // Calculate basic Lambertian lighting
                // float lightIntensity = saturate(dot(normal, lightDir));
                //
                // // Define a base color (e.g., grey)
                // fixed3 albedo = fixed3(0.8, 0.8, 0.8);
                //
                // // Combine lighting and color
                // fixed3 finalColor = albedo * lightIntensity;
                //
                // return fixed4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}