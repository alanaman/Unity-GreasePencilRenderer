using UnityEngine;

public static class SilhouetteShaderIDs
{
    public static readonly int WorldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
    public static readonly int ObjectToWorld = Shader.PropertyToID("_ObjectToWorld");
    public static readonly int ObjectToWorldIt = Shader.PropertyToID("_ObjectToWorldIT");
    public static readonly int NextPointerSrc = Shader.PropertyToID("_nextPointerSrc");
    public static readonly int NextPointerDst = Shader.PropertyToID("_nextPointerDst");
    public static readonly int RankSrc = Shader.PropertyToID("_rankSrc");
    public static readonly int RankDst = Shader.PropertyToID("_rankDst");
    public static readonly int DenseNextPointerSrc = Shader.PropertyToID("_denseNextPointerSrc");
    public static readonly int DenseNextPointerDst = Shader.PropertyToID("_denseNextPointerDst");
    public static readonly int DenseUStrokeSrc = Shader.PropertyToID("_denseUStrokeSrc");
    public static readonly int DenseUStrokeDst = Shader.PropertyToID("_denseUStrokeDst");
    public static readonly int RadiusMultiplier = Shader.PropertyToID("_radiusMultiplier");
    public static readonly int LateralShiftFactor = Shader.PropertyToID("_LateralShiftFactor");
    public static readonly int LateralShiftConstant = Shader.PropertyToID("_LateralShiftConstant");

    public static readonly int GpObjectToWorld = Shader.PropertyToID("_GP_ObjectToWorld");
    public static readonly int GpWorldToObject = Shader.PropertyToID("_GP_WorldToObject");
    public static readonly int GpProj = Shader.PropertyToID("_GP_Proj");
    public static readonly int GpViewProj = Shader.PropertyToID("_GP_ViewProj");
    public static readonly int GpInvViewProj = Shader.PropertyToID("_GP_InvViewProj");
    public static readonly int GpScreenParams = Shader.PropertyToID("_GP_ScreenParams");
}
