Shader "Custom/edgeToStroke"
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
            // adjacency not needed here; we read adj from _inEdges

            struct StrokeData
            {
                float3 pos[2];
                int adj[2];   // Adjacent face index for each endpoint's edge (-1 none, -2 invalid)
                float3 faceNormal;
                uint minPoint[2];
            };
            StructuredBuffer<StrokeData> _inEdges;

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

            // Vertex Shader
            // We use SV_VertexID to get the index of the vertex being processed
            v2f vert (uint vertexID : SV_VertexID)
            {
                v2f o;

                // 1. Use the vertexID to find the correct index from the index buffer
                uint faceIdx = vertexID / 6;

                
                float3 p1 = _inEdges[faceIdx].pos[0];
                float3 p2 = _inEdges[faceIdx].pos[1];
                float3 faceNormal = _inEdges[faceIdx].faceNormal; // fetch face normal if needed

                if (_inEdges[faceIdx].adj[0] < 0 && _inEdges[faceIdx].adj[1] < 0)
                {
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

                bool is_on_p1 = (x == -1.0f);

                int adj;
                if (is_on_p1)
                {
                    adj = _inEdges[faceIdx].adj[0];
                }
                else
                {
                    adj = _inEdges[faceIdx].adj[1];
                }
                
                if (adj < 0)
                {
                    o.vertex = float4(0.0f, 0.0f, -3e36f, 0.0f);
                    o.normalWS = float3(0,0,0);
                    return o;
                }
                
                float3 pos_adj = _inEdges[adj].pos[0];
                if (is_on_p1)
                {
                    if (length(pos_adj-p1) < 0.001)
                    {
                        pos_adj = _inEdges[adj].pos[1];
                    }
                }
                else
                {
                    if (length(pos_adj-p2) < 0.001)
                    {
                        pos_adj = _inEdges[adj].pos[1];
                    }
                }

                float3 wpos_adj = TransformObjectToWorld(pos_adj);
                float3 wpos1 = TransformObjectToWorld(p1);
                float3 wpos2 = TransformObjectToWorld(p2);
                
                float4 ndc_adj = TransformWorldToHClip(wpos_adj);
                float4 ndc1 = TransformWorldToHClip(wpos1.xyz);
                float4 ndc2 = TransformWorldToHClip(wpos2.xyz);

                o.vertex = (is_on_p1) ? ndc1 : ndc2;

                float2 ss_adj = project_to_screenspace(ndc_adj);
                float2 ss1 = project_to_screenspace(ndc1);
                float2 ss2 = project_to_screenspace(ndc2);

                float edge_len;
                float2 edge_dir = safe_normalize_and_get_length(ss2 - ss1, edge_len);
                float2 edge_adj_dir = safe_normalize((is_on_p1) ? (ss1 - ss_adj) : (ss_adj - ss2));

                float radius = 0.05;
                radius = stroke_radius_modulate(radius);
                float clamped_radius = max(0.0f, radius);

                // OUT.uv = float2(x, y) * 0.5f + 0.5f;

                //TODO dot
                // bool is_stroke_start = (p0.mat == -1 && x == -1);
                // bool is_stroke_end = (p3.mat == -1 && x == 1);

                bool is_stroke_endpoint = (adj == -1);

                /* Mitter tangent vector. */
                float2 miter_tan = safe_normalize(edge_adj_dir + edge_dir);
                float miter_dot = dot(miter_tan, edge_adj_dir);
                /* Break corners after a certain angle to avoid really thick corners. */
                const float miter_limit = 0.5f; /* cos(60 degrees) */
                bool miter_break = (miter_dot < miter_limit);
                miter_tan = (miter_break || is_stroke_endpoint) ? edge_dir : (miter_tan / miter_dot);

                /* Rotate 90 degrees counter-clockwise. */
                float2 miter = float2(-miter_tan.y, miter_tan.x);
                
                float3 ss_faceNormal = TransformWorldToHClipDir(faceNormal);
                
                if (dot(ss_faceNormal.xy, miter) < 0.0f)
                {
                    miter = -miter;
                }
                
                float2 screen_ofs = miter * (y+1);

                // /* Reminder: we packed the cap flag into the sign of strength and thickness sign. */
                // if ((is_stroke_start && p1.opacity > 0.0f) || (is_stroke_end && p1.radius > 0.0f) ||
                //     (miter_break && !is_stroke_start && !is_stroke_end))
                // {
                //     screen_ofs += edge_dir * x;
                // }

                
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