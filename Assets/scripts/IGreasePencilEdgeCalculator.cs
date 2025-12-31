using UnityEngine;

namespace DefaultNamespace
{
public interface IGreasePencilEdgeCalculator
{
    public void CalculateEdges();

    public int GetMaximumBufferLength();

    public GraphicsBuffer GetStrokeBuffer();
    public GraphicsBuffer GetColorBuffer();
}
}