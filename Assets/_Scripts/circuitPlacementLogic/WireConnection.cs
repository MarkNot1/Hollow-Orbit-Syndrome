using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class WireConnection : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private readonly List<Vector2Int> routedCells = new();
    private readonly List<BoxCollider> segmentColliders = new();
    private readonly List<Vector3> localRenderPoints = new();
    private readonly List<Vector3> vertices = new();
    private readonly List<int> triangles = new();

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh wireMesh;
    private MaterialPropertyBlock colorProperties;
    private float wireWidth;
    private Vector3 lastStartPosition;
    private Vector3 lastEndPosition;
    private Matrix4x4 lastLocalToWorld;

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
        float hitboxWidthMultiplier,
        bool createColliders)
    {
        StartNode = startNode;
        EndNode = endNode;
        routedCells.Clear();
        if (cells != null)
            routedCells.AddRange(cells);

        ConfigureMesh(localPoints, width, material, color);
        if (createColliders)
            BuildColliders(localPoints, width, hitboxWidthMultiplier);

        StartNode?.Attach(this);
        EndNode?.Attach(this);
    }

    public void UpdatePreview(
        IReadOnlyList<Vector3> localPoints,
        float width,
        Material material,
        Color color)
        => ConfigureMesh(localPoints, width, material, color);

    public void SetAppearance(Material material, Color color)
    {
        EnsureRenderer();
        if (material != null)
            meshRenderer.sharedMaterial = material;

        colorProperties ??= new MaterialPropertyBlock();
        colorProperties.Clear();
        colorProperties.SetColor(BaseColorId, color);
        colorProperties.SetColor(ColorId, color);
        meshRenderer.SetPropertyBlock(colorProperties);
    }

    public bool Connects(CircuitNode first, CircuitNode second)
        => StartNode == first && EndNode == second || StartNode == second && EndNode == first;

    public CircuitNode GetOther(CircuitNode node)
        => StartNode == node ? EndNode : EndNode == node ? StartNode : null;

    public bool ReplaceEndpoint(CircuitNode oldNode, CircuitNode newNode)
    {
        if (oldNode == null || newNode == null || oldNode == newNode)
            return false;

        if (StartNode == oldNode)
        {
            oldNode.Detach(this);
            StartNode = newNode;
            newNode.Attach(this);
        }
        else if (EndNode == oldNode)
        {
            oldNode.Detach(this);
            EndNode = newNode;
            newNode.Attach(this);
        }
        else
        {
            return false;
        }

        RebuildMesh();
        oldNode.SlotOwner?.RefreshLabels();
        newNode.SlotOwner?.RefreshLabels();
        return true;
    }

    public void Disconnect()
    {
        StartNode?.Detach(this);
        EndNode?.Detach(this);
        StartNode = null;
        EndNode = null;
    }

    private void OnDestroy()
    {
        if (wireMesh != null)
            Destroy(wireMesh);
    }

    private void LateUpdate()
    {
        if (StartNode == null || EndNode == null)
            return;

        Vector3 start = StartNode.ConnectionPoint.position;
        Vector3 end = EndNode.ConnectionPoint.position;
        Matrix4x4 matrix = transform.localToWorldMatrix;
        if ((start - lastStartPosition).sqrMagnitude > 0.00000001f ||
            (end - lastEndPosition).sqrMagnitude > 0.00000001f || matrix != lastLocalToWorld)
        {
            RebuildMesh();
        }
    }

    private void ConfigureMesh(
        IReadOnlyList<Vector3> points,
        float width,
        Material material,
        Color color)
    {
        EnsureRenderer();
        wireWidth = Mathf.Max(width, 0.0005f);
        SetAppearance(material, color);

        localRenderPoints.Clear();
        if (points != null)
            localRenderPoints.AddRange(points);
        RebuildMesh();
    }

    private void EnsureRenderer()
    {
        meshFilter ??= GetComponent<MeshFilter>();
        meshRenderer ??= GetComponent<MeshRenderer>();
        if (wireMesh != null)
            return;

        wireMesh = new Mesh { name = "Circuit Wire Mesh" };
        wireMesh.MarkDynamic();
        meshFilter.sharedMesh = wireMesh;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private void RebuildMesh()
    {
        EnsureRenderer();
        vertices.Clear();
        triangles.Clear();

        if (localRenderPoints.Count >= 2)
        {
            Vector3 normal = transform.up.normalized;
            float halfWidth = wireWidth * 0.5f;
            float halfHeight = Mathf.Max(wireWidth * 0.18f, 0.0003f);

            for (int i = 0; i < localRenderPoints.Count - 1; i++)
            {
                Vector3 start = GetWorldPoint(i);
                Vector3 end = GetWorldPoint(i + 1);
                Vector3 delta = end - start;
                if (delta.sqrMagnitude < 0.00000001f)
                    continue;

                Vector3 direction = delta.normalized;
                Vector3 side = Vector3.Cross(normal, direction).normalized;
                if (side.sqrMagnitude < 0.5f)
                    side = transform.right.normalized;

                // Slight overlap closes routed elbows without the twisting and
                // view-dependent width produced by the previous LineRenderer.
                start -= direction * halfWidth;
                end += direction * halfWidth;
                AddBox(start, end, side * halfWidth, normal * halfHeight);
            }
        }

        wireMesh.Clear();
        wireMesh.SetVertices(vertices);
        wireMesh.SetTriangles(triangles, 0);
        wireMesh.RecalculateBounds();
        lastStartPosition = StartNode != null ? StartNode.ConnectionPoint.position : Vector3.zero;
        lastEndPosition = EndNode != null ? EndNode.ConnectionPoint.position : Vector3.zero;
        lastLocalToWorld = transform.localToWorldMatrix;
        transform.hasChanged = false;
    }

    private Vector3 GetWorldPoint(int index)
    {
        if (index == 0 && StartNode != null)
            return StartNode.ConnectionPoint.position;
        if (index == localRenderPoints.Count - 1 && EndNode != null)
            return EndNode.ConnectionPoint.position;
        return transform.TransformPoint(localRenderPoints[index]);
    }

    private void AddBox(Vector3 worldStart, Vector3 worldEnd, Vector3 side, Vector3 normal)
    {
        int first = vertices.Count;
        AddVertex(worldStart - side - normal);
        AddVertex(worldStart + side - normal);
        AddVertex(worldStart + side + normal);
        AddVertex(worldStart - side + normal);
        AddVertex(worldEnd - side - normal);
        AddVertex(worldEnd + side - normal);
        AddVertex(worldEnd + side + normal);
        AddVertex(worldEnd - side + normal);

        AddQuad(first, 0, 1, 2, 3);
        AddQuad(first, 7, 6, 5, 4);
        AddQuad(first, 0, 4, 5, 1);
        AddQuad(first, 1, 5, 6, 2);
        AddQuad(first, 2, 6, 7, 3);
        AddQuad(first, 3, 7, 4, 0);
    }

    private void AddVertex(Vector3 worldPosition)
        => vertices.Add(transform.InverseTransformPoint(worldPosition));

    private void AddQuad(int first, int a, int b, int c, int d)
    {
        triangles.Add(first + a);
        triangles.Add(first + b);
        triangles.Add(first + c);
        triangles.Add(first + a);
        triangles.Add(first + c);
        triangles.Add(first + d);
    }

    private void BuildColliders(
        IReadOnlyList<Vector3> points,
        float width,
        float hitboxWidthMultiplier)
    {
        int wireLayer = LayerMask.NameToLayer("CircuitWire");
        if (wireLayer >= 0)
            gameObject.layer = wireLayer;

        float worldHitboxWidth = Mathf.Max(
            width * Mathf.Max(1f, hitboxWidthMultiplier),
            0.025f);

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
            Vector3 worldScale = segment.transform.lossyScale;
            collider.size = new Vector3(
                delta.magnitude,
                worldHitboxWidth / Mathf.Max(Mathf.Abs(worldScale.y), 0.0001f),
                worldHitboxWidth / Mathf.Max(Mathf.Abs(worldScale.z), 0.0001f));
            segmentColliders.Add(collider);
        }
    }
}
