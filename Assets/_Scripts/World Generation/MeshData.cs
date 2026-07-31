using UnityEngine;
using System.Collections.Generic;

public class MeshData
{
    public List<Vector3> vertices = new List<Vector3>();
    public List<int> triangles = new List<int>();
    public List<Vector2> uvs = new List<Vector2>();
    public List<Vector2> tiledUvs = new List<Vector2>();
    public List<Vector3> normals = new List<Vector3>();
    public List<Color32> colors = new List<Color32>();

    public List<Vector3> colliderVertices = new List<Vector3>();
    public List<int> colliderTriangles = new List<int>();

    private readonly bool collectCollider;

    public MeshData(bool collectCollider = true, int estimatedQuads = 0)
    {
        this.collectCollider = collectCollider;
        if (estimatedQuads <= 0)
            return;

        int vertexCapacity = estimatedQuads * 4;
        int triangleCapacity = estimatedQuads * 6;
        vertices.Capacity = vertexCapacity;
        triangles.Capacity = triangleCapacity;
        uvs.Capacity = vertexCapacity;
        tiledUvs.Capacity = vertexCapacity;
        normals.Capacity = vertexCapacity;
        colors.Capacity = vertexCapacity;
        if (collectCollider)
        {
            colliderVertices.Capacity = vertexCapacity;
            colliderTriangles.Capacity = triangleCapacity;
        }
    }

    public void AddVertex(Vector3 vertex, bool vertexGeneratesCollider)
    {
        vertices.Add(vertex);
        if (collectCollider && vertexGeneratesCollider)
        {
            colliderVertices.Add(vertex);
        }
    }

    public void AddQuadTriangles(bool quadGeneratesCollider, bool splitAlternative = false)
    {
        if (splitAlternative)
        {
            triangles.Add(vertices.Count - 4);
            triangles.Add(vertices.Count - 3);
            triangles.Add(vertices.Count - 1);

            triangles.Add(vertices.Count - 3);
            triangles.Add(vertices.Count - 2);
            triangles.Add(vertices.Count - 1);
            if (collectCollider && quadGeneratesCollider)
            {
                colliderTriangles.Add(colliderVertices.Count - 4);
                colliderTriangles.Add(colliderVertices.Count - 3);
                colliderTriangles.Add(colliderVertices.Count - 1);
                colliderTriangles.Add(colliderVertices.Count - 3);
                colliderTriangles.Add(colliderVertices.Count - 2);
                colliderTriangles.Add(colliderVertices.Count - 1);
            }
        }
        else
        {
            triangles.Add(vertices.Count - 4);
            triangles.Add(vertices.Count - 3);
            triangles.Add(vertices.Count - 2);

            triangles.Add(vertices.Count - 4);
            triangles.Add(vertices.Count - 2);
            triangles.Add(vertices.Count - 1);
            if (collectCollider && quadGeneratesCollider)
            {
                colliderTriangles.Add(colliderVertices.Count - 4);
                colliderTriangles.Add(colliderVertices.Count - 3);
                colliderTriangles.Add(colliderVertices.Count - 2);
                colliderTriangles.Add(colliderVertices.Count - 4);
                colliderTriangles.Add(colliderVertices.Count - 2);
                colliderTriangles.Add(colliderVertices.Count - 1);
            }
        }
    }

    public void AddQuadNormals(bool splitAlternative = false)
    {
        int first = vertices.Count - 4;
        Vector3 v0 = vertices[first];
        Vector3 v1 = vertices[first + 1];
        Vector3 v2 = vertices[first + 2];
        Vector3 v3 = vertices[first + 3];
        if (splitAlternative)
        {
            Vector3 a = FaceNormal(v0, v1, v3);
            Vector3 b = FaceNormal(v1, v2, v3);
            normals.Add(NormalizedOrUp(a));
            normals.Add(NormalizedOrUp(a + b));
            normals.Add(NormalizedOrUp(b));
            normals.Add(NormalizedOrUp(a + b));
        }
        else
        {
            Vector3 a = FaceNormal(v0, v1, v2);
            Vector3 b = FaceNormal(v0, v2, v3);
            normals.Add(NormalizedOrUp(a + b));
            normals.Add(NormalizedOrUp(a));
            normals.Add(NormalizedOrUp(a + b));
            normals.Add(NormalizedOrUp(b));
        }
    }

    private static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        return normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.zero;
    }

    private static Vector3 NormalizedOrUp(Vector3 normal)
    {
        return normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
    }

    public void CalculateNormals()
    {
        normals.Clear();
        for (int i = 0; i < vertices.Count; i++)
            normals.Add(Vector3.zero);

        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (normal.sqrMagnitude <= 0.000001f)
                continue;
            normal.Normalize();
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }

        for (int i = 0; i < normals.Count; i++)
            normals[i] = normals[i].sqrMagnitude > 0.000001f
                ? normals[i].normalized
                : Vector3.up;
    }

}
