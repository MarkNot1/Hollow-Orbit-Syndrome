using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public sealed class WireConnection : MonoBehaviour
{
    private readonly List<Vector2Int> routedCells = new();
    private readonly List<BoxCollider> segmentColliders = new();
    private readonly List<Vector3> localRenderPoints = new();
    private LineRenderer lineRenderer;

    public CircuitNode StartNode { get; private set; }
    public CircuitNode EndNode { get; private set; }
    public IReadOnlyList<Vector2Int> RoutedCells => routedCells;

    public void Initialize(
        CircuitNode startNode,
        CircuitNode endNode,
        IReadOnlyList<Vector2Int> cells,
        IReadOnlyList<Vector3> localPoints,
        float width,
        Material material,
        Color color,
        bool createColliders)
    {
        StartNode = startNode;
        EndNode = endNode;
        routedCells.Clear();
        if (cells != null)
            routedCells.AddRange(cells);

        ConfigureLine(localPoints, width, material, color);
        if (createColliders)
            BuildColliders(localPoints, width);

        StartNode?.Attach(this);
        EndNode?.Attach(this);
    }

    public void UpdatePreview(
        IReadOnlyList<Vector3> localPoints,
        float width,
        Material material,
        Color color)
        => ConfigureLine(localPoints, width, material, color);

    public bool Connects(CircuitNode first, CircuitNode second)
        => StartNode == first && EndNode == second || StartNode == second && EndNode == first;

    public void Disconnect()
    {
        StartNode?.Detach(this);
        EndNode?.Detach(this);
        StartNode = null;
        EndNode = null;
    }

    private void LateUpdate()
    {
        if (lineRenderer == null || !transform.hasChanged)
            return;

        ApplyWorldPositions();
    }

    private void ConfigureLine(
        IReadOnlyList<Vector3> points,
        float width,
        Material material,
        Color color)
    {
        lineRenderer ??= GetComponent<LineRenderer>();
        // TileGrid is non-uniformly scaled. World-space rendering prevents that
        // scale from stretching the line ribbon and changing its apparent width.
        lineRenderer.useWorldSpace = true;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.loop = false;
        lineRenderer.numCornerVertices = 0;
        lineRenderer.numCapVertices = 0;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        if (material != null)
            lineRenderer.sharedMaterial = material;

        localRenderPoints.Clear();
        if (points != null)
        {
            for (int i = 0; i < points.Count; i++)
                localRenderPoints.Add(points[i]);
        }

        ApplyWorldPositions();
    }

    private void ApplyWorldPositions()
    {
        lineRenderer.positionCount = localRenderPoints.Count;
        for (int i = 0; i < localRenderPoints.Count; i++)
            lineRenderer.SetPosition(i, transform.TransformPoint(localRenderPoints[i]));
        transform.hasChanged = false;
    }

    private void BuildColliders(IReadOnlyList<Vector3> points, float width)
    {
        int wireLayer = LayerMask.NameToLayer("CircuitWire");
        if (wireLayer >= 0)
            gameObject.layer = wireLayer;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 delta = points[i + 1] - points[i];
            if (delta.sqrMagnitude < 0.000001f)
                continue;

            GameObject segment = new($"Segment {i + 1}");
            segment.layer = wireLayer >= 0 ? wireLayer : gameObject.layer;
            segment.transform.SetParent(transform, false);
            segment.transform.localPosition = (points[i] + points[i + 1]) * 0.5f;
            segment.transform.localRotation = Quaternion.FromToRotation(Vector3.right, delta.normalized);
            BoxCollider collider = segment.AddComponent<BoxCollider>();
            collider.size = new Vector3(delta.magnitude, Mathf.Max(width * 2f, 0.02f), Mathf.Max(width * 2f, 0.02f));
            segmentColliders.Add(collider);
        }
    }
}
