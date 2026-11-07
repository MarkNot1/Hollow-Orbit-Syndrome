using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class FramePlacementPreview : System.IDisposable
{
    private readonly GameObject root;
    private readonly Mesh mesh;
    private readonly Material material;
    private StructureData blueprint;

    public FramePlacementPreview(Material source)
    {
        root = new GameObject("Frame placement preview") { layer = 2 };
        mesh = new Mesh { name = "Frame preview", indexFormat = IndexFormat.UInt32 };
        material = new Material(source);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    public void Show(StructureData data, Vector3 position, Quaternion rotation, bool valid)
    {
        if (blueprint != data)
        {
            blueprint = data;
            HashSet<Vector3Int> cells = new();
            foreach (var voxel in data.voxels) cells.Add(voxel.position);
            MeshData geometry = new(false);
            foreach (var voxel in data.voxels)
                for (int i = 0; i < VoxelStructure.FaceNormals.Length; i++)
                    if (!cells.Contains(voxel.position + VoxelStructure.FaceNormals[i]))
                        VoxelHelper.AddCubeFace(VoxelStructure.FaceDirections[i], voxel.position, voxel.type, geometry);
            mesh.Clear();
            mesh.SetVertices(geometry.vertices);
            mesh.SetTriangles(geometry.triangles, 0);
            mesh.RecalculateBounds();
        }
        material.color = valid ? new Color(0.2f, 1f, 0.55f, 0.4f) : new Color(1f, 0.15f, 0.1f, 0.4f);
        root.transform.SetPositionAndRotation(position, rotation);
        root.SetActive(true);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
    }

    public void Dispose()
    {
        Hide();
        VoxelStructure.Release(root);
        VoxelStructure.Release(mesh);
        VoxelStructure.Release(material);
    }
}
