using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public sealed class StructureChunk : MonoBehaviour
{
    public const int Size = 16;
    internal readonly Dictionary<Vector3Int, VoxelType> Voxels = new();
    public VoxelStructure Structure { get; private set; }
    public Vector3Int Origin { get; private set; }
    private Mesh mesh;

    internal void Initialize(VoxelStructure structure, Vector3Int origin, Material material)
    {
        Structure = structure;
        Origin = origin;
        transform.localPosition = (Vector3)origin * VoxelMetrics.Size;
        GetComponent<MeshRenderer>().sharedMaterial = material;
        mesh = new Mesh { name = "Structure voxels", indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    internal void Rebuild()
    {
        MeshData data = new(false);
        foreach (var voxel in Voxels)
        {
            for (int i = 0; i < VoxelStructure.FaceNormals.Length; i++)
            {
                if (!VoxelRuntimeData.IsFaceVisible(voxel.Value,
                    Structure.GetVoxel(Origin + voxel.Key + VoxelStructure.FaceNormals[i])))
                    continue;
                VoxelHelper.AddCubeFace(VoxelStructure.FaceDirections[i], voxel.Key, voxel.Value, data);
            }
        }
        MeshCollider collider = GetComponent<MeshCollider>();
        collider.sharedMesh = null;
        mesh.Clear();
        mesh.SetVertices(data.vertices);
        mesh.SetNormals(data.normals);
        mesh.SetUVs(0, data.uvs);
        mesh.SetUVs(1, data.tiledUvs);
        mesh.SetColors(data.colors);
        mesh.SetTriangles(data.triangles, 0);
        mesh.RecalculateBounds();
        if (data.triangles.Count > 0) collider.sharedMesh = mesh;
    }

    private void OnDestroy()
    {
        if (mesh != null) VoxelStructure.Release(mesh);
    }
}
