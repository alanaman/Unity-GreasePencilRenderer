#define INVALID_UINT 0xFFFFFFFFu
#define ADJ_NONE -1
#define INVALID -2

#define STROKE_FLAG_CYCLIC 1<<0
//if this stroke point has a parent, 0 otherwise
#define STROKE_FLAG_IS_CHILD 1<<1
#define STROKE_FLAG_IS_INVALID 1<<2


struct StrokeData
{
    float3 pos; // Endpoints of the silhouette edge on this face
    int adj;    // Adjacent face index for each endpoint's edge (-1: no neighbor, -2: invalid stroke)
    float3 faceNormal; // face normal in world space
    
    uint minPoint; // tail of this stroke
    
    uint rank;            // hop count to tail
    uint flags;
    float distFromTail;   // cumulative geometric distance to tail (0 at tail)
    
    uint totalStrokeLength; // total length of the stroke that contains this point
    
    uint strokePointsOffset; // Offset to the stroke points array
};

inline bool IsCyclic(StrokeData s)
{
    return s.flags & STROKE_FLAG_CYCLIC;
}
inline bool IsChild(StrokeData s)
{
    return s.flags & STROKE_FLAG_IS_CHILD;
}
inline bool IsInvalid(StrokeData s)
{
    return s.flags & STROKE_FLAG_IS_INVALID;
}

// struct SharpStrokeData
// {
//     uint vertIdx; // Endpoints of the silhouette edge on this face
//     int adj;   // Adjacent face index for each endpoint's edge (-1: no neighbor, -2: invalid stroke)
//     
//     uint minPoint; // tail of this stroke
//     
//     uint rank;            // hop count to tail
//     uint isCyclic;
//     float distFromTail;   // cumulative geometric distance to tail (0 at tail)
//     
//     uint isChild; // 1 if this stroke point has a parent, 0 otherwise
//     uint totalStrokeLength; // total length of the stroke that contains this point
//     
//     uint strokeIdx; // ID of the stroke this point belongs to
//     uint strokePointsOffset; // Offset to the stroke points array
// };
