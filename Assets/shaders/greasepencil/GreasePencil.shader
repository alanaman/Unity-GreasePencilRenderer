Shader "Custom/GreasePencil"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white"
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            cull off
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "gpencil_vert_debug.hlsl"
            #include "gpencil_frag.hlsl"

            ENDHLSL
        }
    }
}
