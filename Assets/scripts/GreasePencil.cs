using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

// This file defines the ScriptableObject that will hold the deserialized
// Grease Pencil data. This allows the data to be saved as a persistent
// asset in your Unity project, making it easy to reference in other
// components and scenes.
[CreateAssetMenu(fileName = "GreasePencilData", menuName = "Grease Pencil/Grease Pencil Data", order = 1)]
public class GreasePencilSO : ScriptableObject
{
    // The main data object that holds all the information from the JSON.
    public GreasePencilData data;
}

[System.Serializable]
public class GreasePencilData
{
    public List<MaterialData> materials;
    public List<LayerData> layers;
}

[System.Serializable]
public class MaterialData
{
    public string name;
    public float[] stroke_color;
    public float[] fill_color;
    public float[] fill_mix_color;
    public float[] fill_uv_rot_scale;
    public float[] fill_uv_offset;
    public float[] alignment_rot;
    public float stroke_texture_mix;
    public float stroke_u_scale;
    public float fill_texture_mix;
    public int flag;
}

[System.Serializable]
public class LayerData
{
    public string name;
    public float[] tint_color;
    public float tint_factor;
    public float opacity;
    public List<FrameData> frames;

    //Update when adding triangle/fill support 
    public List<int3> Triangles()
    {
        return new List<int3>();
    } 
}

[System.Serializable]
public class FrameData
{
    public int frame_number;
    public List<StrokeData> strokes;
}

[System.Serializable]
public class StrokeData
{
    public int material_index;
    public float aspect_ratio;
    public bool cyclic;
    public int end_cap;
    public int start_cap;
    public float softness;
    public float[] fill_color;
    public float fill_opacity;
    public List<PointData> points;
}

[System.Serializable]
public class PointData
{
    public float[] position;
    public float radius;
    public float opacity;
    public float rotation;
    public float[] vertex_color;
    
    public Vector3 Position =>  new Vector3(position[0], position[1], position[2]);
    public Vector4 VertexColor => new Vector4(vertex_color[0], vertex_color[1], vertex_color[2], vertex_color[3]);
}