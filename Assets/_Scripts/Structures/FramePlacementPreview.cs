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

    public void Show(StructureData data, Vector3 position, Quaternion rotation, bool valid, SpecialPrefabDatabaseSO prefabs = null)
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
            if (prefabs != null && data.prefabs.Count > 0)
            {
                Mesh hull = Object.Instantiate(mesh);
                var pieces = new List<CombineInstance> { new() { mesh = hull, transform = Matrix4x4.identity } };
                foreach (var record in data.prefabs)
                {
                    prefabs.TryGetPrefab(record.prefabId, out SpecialPrefabData prefab);
                    Matrix4x4 placement = Matrix4x4.TRS(record.position, record.rotation, record.scale);
                    foreach (MeshFilter filter in prefab.Prefab.GetComponentsInChildren<MeshFilter>())
                        if (filter.sharedMesh != null)
                            for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                                pieces.Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = submesh,
                                    transform = placement * prefab.Prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                }
                mesh.CombineMeshes(pieces.ToArray());
                VoxelStructure.Release(hull);
            }
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
