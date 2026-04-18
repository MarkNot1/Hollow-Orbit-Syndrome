using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEditor;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]


public class ChunkRenderer : MonoBehaviour
{
    MeshFilter meshFilter;
    MeshCollider meshCollider;
    Mesh mesh;
    Mesh collisionMesh;
    public bool showGizmo = false;

    public ChunkData ChunkData { get; private set; }

    public bool ModifiedByThePlayer
    {
        get
        {
            return ChunkData.modifiedByThePlayer;
        }
        set
        {
            ChunkData.modifiedByThePlayer = value;
        }
    }

    static PhysicsMaterial s_voxelTerrainMaterial;

    static PhysicsMaterial GetVoxelTerrainMaterial()
    {
        if (s_voxelTerrainMaterial == null)
        {
            s_voxelTerrainMaterial = new PhysicsMaterial("VoxelTerrain")
            {
                bounciness = 0f,
                dynamicFriction = 0.55f,
                staticFriction = 0.55f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        return s_voxelTerrainMaterial;
    }

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
        mesh = meshFilter.mesh;
        collisionMesh = new Mesh();
        meshCollider.convex = false;
        meshCollider.sharedMaterial = GetVoxelTerrainMaterial();
        meshCollider.enabled = false;
    }

    public void InitializeChunk(ChunkData data)
    {
        this.ChunkData = data;
    }


    private void RenderMesh(MeshData meshData)
    {
        mesh.Clear();

        mesh.subMeshCount = 2;
        mesh.vertices = meshData.vertices.Concat(meshData.waterMesh.vertices).ToArray();

        mesh.SetTriangles(meshData.triangles.ToArray(), 0);
        mesh.SetTriangles(meshData.waterMesh.triangles.Select(val => val + meshData.vertices.Count).ToArray(), 1);

        mesh.uv = meshData.uvs.Concat(meshData.waterMesh.uvs).ToArray();
        mesh.RecalculateNormals();

        meshCollider.sharedMesh = null;
        collisionMesh.Clear();
        
        if (meshData.vertices.Count > 0)
        {
            collisionMesh.vertices = meshData.vertices.ToArray();
            collisionMesh.triangles = meshData.triangles.ToArray();
            collisionMesh.RecalculateNormals();
            meshCollider.sharedMesh = collisionMesh;
        }
    }

    public void SetCollisionActive(bool isActive)
    {
        if (meshCollider != null)
        {
            if (isActive && (meshCollider.sharedMesh == null || meshCollider.sharedMesh.vertexCount == 0))
            {
                meshCollider.enabled = false;
            }
            else
            {
                meshCollider.enabled = isActive;
            }
        }
    }

    public void UpdateChunk()
    {
        RenderMesh(Chunk.GetChunkMeshData(ChunkData));
    }

    public void UpdateChunk(MeshData data)
    {
        RenderMesh(data);
    }

#if UNITY_EDITOR

    private void OnDrawGizmos()
    {
        if (showGizmo)
        {
            if (Application.isPlaying && ChunkData != null)
            {
                if (Selection.activeObject == gameObject)
                    Gizmos.color = new Color(0, 1, 0, 0.4f);
                else
                    Gizmos.color = new Color(1, 0, 1, 0.4f);

                float s = VoxelMetrics.Size;
                Vector3 ext = new Vector3(ChunkData.chunkSize * s, ChunkData.chunkHeight * s, ChunkData.chunkSize * s);
                Gizmos.DrawCube(transform.position + (ext - Vector3.one * s) * 0.5f, ext);
            }
        }
    }
#endif
}
