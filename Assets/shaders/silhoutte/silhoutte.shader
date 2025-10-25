Shader "Custom/silhoutte"
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
            Cull Back
            
            HLSLPROGRAM

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0
            #pragma require geometry
                        
            #pragma vertex vert
            #pragma geometry Geometry
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct VertexOutput {
                float4 positionWS   : SV_POSITION; // Position in world space
                float3 normalWS : NORMAL;
                float2 uv           : TEXCOORD1; // UVs
            };

            struct GeometryOutput {
                float4 positionCS               : SV_POSITION; // Position in clip space
                float3 positionWS               : POSITION_WS; // Position in world space
                float3 normalWS                 : NORMAL_WS; // Normal vector in world space
                float2 uv                       : TEXCOORD2; // UVs
                float3 normalCS : NORMAL;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            VertexOutput vert(Attributes IN)
            {
                VertexOutput OUT;
                OUT.positionWS = float4(TransformObjectToWorld(IN.positionOS.xyz),1);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            GeometryOutput SetupVertex(float3 positionWS, float3 normalWS, float2 uv) {
                // Setup an output struct
                GeometryOutput output;
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.uv = uv;
                // This function calculates clip space position, taking the shadow caster pass into account
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalCS = TransformWorldToHClipDir(normalWS);
                return output;
            }

            bool TryGetZeroPoint(float3 p1, float3 p2, float dot1, float dot2, out float3 zeroPoint) {
                zeroPoint = float3(0,0,0);
                if (dot1 * dot2 > 0) {
                    return false;
                }
                float t = dot1 / (dot1 - dot2);
                zeroPoint = lerp(p1, p2, t);
                return true;
            }
            
            [maxvertexcount(9)]
            void Geometry(triangle VertexOutput inputs[3], inout TriangleStream<GeometryOutput> outputStream) {
                // outputStream.RestartStrip();
                // outputStream.Append(SetupVertex(inputs[0].positionWS, inputs[0].normalWS, inputs[0].uv));
                // outputStream.Append(SetupVertex(inputs[1].positionWS, inputs[1].normalWS, inputs[1].uv));
                // outputStream.Append(SetupVertex(inputs[2].positionWS, inputs[2].normalWS, inputs[2].uv));

                float3 dirToCam0 = normalize(_WorldSpaceCameraPos - inputs[0].positionWS);
                float3 dirToCam1 = normalize(_WorldSpaceCameraPos - inputs[1].positionWS);
                float3 dirToCam2 = normalize(_WorldSpaceCameraPos - inputs[2].positionWS);

                float dot0 = dot(inputs[0].normalWS, dirToCam0);
                float dot1 = dot(inputs[1].normalWS, dirToCam1);
                float dot2 = dot(inputs[2].normalWS, dirToCam2);

                int index = 0;
                float3 zero1;
                bool h1 = TryGetZeroPoint(inputs[0].positionWS, inputs[1].positionWS, dot0, dot1, zero1);
                float3 zero2;
                bool h2 = TryGetZeroPoint(inputs[1].positionWS, inputs[2].positionWS, dot1, dot2, zero2);
                float3 zero3;
                bool h3 = TryGetZeroPoint(inputs[2].positionWS, inputs[0].positionWS, dot2, dot0, zero3);
                if (h1 && h2) {
                    outputStream.RestartStrip();
                    outputStream.Append(SetupVertex(zero1, inputs[0].normalWS, inputs[0].uv));
                    outputStream.Append(SetupVertex(inputs[1].positionWS, inputs[1].normalWS, inputs[1].uv));
                    outputStream.Append(SetupVertex(zero2, inputs[2].normalWS, inputs[2].uv));
                }
                if (h2 && h3) {
                    outputStream.RestartStrip();
                    outputStream.Append(SetupVertex(zero2, inputs[1].normalWS, inputs[1].uv));
                    outputStream.Append(SetupVertex(inputs[2].positionWS, inputs[2].normalWS, inputs[2].uv));
                    outputStream.Append(SetupVertex(zero3, inputs[0].normalWS, inputs[0].uv));
                }
                if (h3 && h1) {
                    outputStream.RestartStrip();
                    outputStream.Append(SetupVertex(zero3, inputs[2].normalWS, inputs[2].uv));
                    outputStream.Append(SetupVertex(inputs[0].positionWS, inputs[0].normalWS, inputs[0].uv));
                    outputStream.Append(SetupVertex(zero1, inputs[0].normalWS, inputs[0].uv));
                }
            }

            
            half4 frag(GeometryOutput IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                return color;
            }
            ENDHLSL
        }
    }
}
