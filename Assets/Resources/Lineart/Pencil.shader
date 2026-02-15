Shader "Lineart/Pencil"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white"
        _LateralShiftFactor("Lateral Shift Factor", Float) = 0.0
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

            #include "Assets/Resources/GreasePencil/gpencil_vert.hlsl"
            #include "Assets/Resources/Lineart/lineart_pencil_frag.hlsl"

            ENDHLSL
        }
    }
}
