using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class ChunkRenderer : MonoBehaviour
{
    public readonly struct CollisionPreparation
    {
        public readonly int meshInstanceId;
        public readonly int leaseId;
        public readonly int buildId;
        public readonly int contentVersion;

        public CollisionPreparation(int meshInstanceId, int leaseId, int buildId, int contentVersion)
        {
            this.meshInstanceId = meshInstanceId;
            this.leaseId = leaseId;
            this.buildId = buildId;
            this.contentVersion = contentVersion;
        }

        public bool IsValid => meshInstanceId != 0;
    }

    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private MeshRenderer meshRenderer;
    private Mesh renderMesh;
    private Mesh collisionMeshA;
    private Mesh collisionMeshB;
    private Mesh preparedCollisionMesh;
    private Mesh activeCollisionMesh;
    private bool collisionRequested;
    private int collisionBuildId;
    private int activeCollisionContentVersion = -1;
    private int collisionResolvedContentVersion = -1;
    private string lastCollisionActivationFailure;
    private int lastPreparedCollisionVertexCount;
    private int lastPreparedCollisionTriangleCount;
    private static int configuredAtlasMaterialId;

    public bool showGizmo;
    public ChunkData ChunkData { get; private set; }
    public ChunkLodLevel ActiveLod { get; private set; } = ChunkLodLevel.Full;
    public int AppliedContentVersion { get; private set; } = -1;
    public bool IsCollisionActive => meshCollider != null && meshCollider.enabled &&
                                     meshCollider.sharedMesh != null;
    public int ActiveCollisionContentVersion => activeCollisionContentVersion;
    public int CollisionResolvedContentVersion => collisionResolvedContentVersion;
    public bool HasAllocatedCollisionMeshes => collisionMeshA != null || collisionMeshB != null;
    public bool HasReusableCollision => activeCollisionMesh != null && activeCollisionContentVersion >= 0;
    public string LastCollisionActivationFailure => lastCollisionActivationFailure;
    public int LastPreparedCollisionVertexCount => lastPreparedCollisionVertexCount;
    public int LastPreparedCollisionTriangleCount => lastPreparedCollisionTriangleCount;
    public int LeaseId { get; private set; }

    public bool ModifiedByThePlayer
    {
        get => ChunkData != null && ChunkData.modifiedByThePlayer;
        set { if (ChunkData != null) ChunkData.modifiedByThePlayer = value; }
    }

    private static PhysicsMaterial voxelTerrainMaterial;

    private static PhysicsMaterial GetVoxelTerrainMaterial()
    {
        if (voxelTerrainMaterial == null)
        {
            voxelTerrainMaterial = new PhysicsMaterial("VoxelTerrain")
            {
                bounciness = 0f,
                dynamicFriction = 0.55f,
                staticFriction = 0.55f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }
        return voxelTerrainMaterial;
    }

    private void Awake()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (meshFilter != null && meshRenderer != null && meshCollider != null && renderMesh != null)
            return;

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        meshRenderer.allowOcclusionWhenDynamic = true;
        Material sharedMaterial = meshRenderer.sharedMaterial;
        if (sharedMaterial != null && sharedMaterial.GetInstanceID() != configuredAtlasMaterialId &&
            sharedMaterial.HasProperty("_VoxelAtlasTileSize"))
        {
            sharedMaterial.SetVector("_VoxelAtlasTileSize", new Vector4(
                VoxelRuntimeData.TileSizeX,
                VoxelRuntimeData.TileSizeY,
                0f,
                0f));
            sharedMaterial.SetFloat("_VoxelAtlasInset", VoxelDataManager.textureOffset);
            configuredAtlasMaterialId = sharedMaterial.GetInstanceID();
        }
        meshCollider = GetComponent<MeshCollider>();
        renderMesh = meshFilter.sharedMesh;
        if (renderMesh == null || renderMesh.name != "Chunk Render Mesh")
        {
            renderMesh = new Mesh { name = "Chunk Render Mesh" };
            meshFilter.sharedMesh = renderMesh;
        }
        renderMesh.indexFormat = IndexFormat.UInt32;
        renderMesh.MarkDynamic();
        meshCollider.convex = false;
        meshCollider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
                                      MeshColliderCookingOptions.EnableMeshCleaning |
                                      MeshColliderCookingOptions.WeldColocatedVertices |
                                      MeshColliderCookingOptions.UseFastMidphase;
        meshCollider.sharedMaterial = GetVoxelTerrainMaterial();
        meshCollider.enabled = false;
        SetFarRenderMode(false);
    }

    private static Mesh CreateCollisionMesh(string meshName)
    {
        return new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
    }

    public void InitializeChunk(ChunkData data)
    {
        EnsureInitialized();
        LeaseId++;
        ChunkData = data;
        ActiveLod = ChunkLodLevel.Full;
        AppliedContentVersion = -1;
        collisionRequested = false;
        collisionBuildId = 0;
        preparedCollisionMesh = null;
        activeCollisionMesh = null;
        activeCollisionContentVersion = -1;
        collisionResolvedContentVersion = -1;
        lastCollisionActivationFailure = null;
        lastPreparedCollisionVertexCount = 0;
        lastPreparedCollisionTriangleCount = 0;
        meshCollider.enabled = false;
        meshCollider.sharedMesh = null;
        renderMesh.Clear(false);
        collisionMeshA?.Clear(false);
        collisionMeshB?.Clear(false);
        SetFarRenderMode(false);
    }

    public CollisionPreparation ApplyMesh(
        MeshData data,
        ChunkLodLevel lod,
        int contentVersion,
        bool requestCollision)
    {
        ApplyRenderMesh(data);
        ActiveLod = lod;
        AppliedContentVersion = contentVersion;
        // Keep one render state across tiers; shadow-state changes made the LOD
        // boundary visible as a dark square even when geometry matched.
        SetFarRenderMode(false);

        if (!requestCollision)
        {
            // LOD-only rebuilds must not throw away a cooked collider. Keeping the
            // inactive mesh lets a revisited chunk reactivate collision instantly.
            // A content edit still invalidates the old geometry.
            if (activeCollisionContentVersion >= 0 &&
                activeCollisionContentVersion != contentVersion)
                ClearCollisionCache();
            return default;
        }
        collisionRequested = true;
        collisionResolvedContentVersion = -1;
        if (data.colliderVertices.Count == 0)
        {
            // An edited-away surface must not retain the previous collider.
            ClearCollisionCache();
            collisionRequested = true;
            collisionResolvedContentVersion = contentVersion;
            return default;
        }

        EnsureCollisionMeshes();
        Mesh target = activeCollisionMesh == collisionMeshA ? collisionMeshB : collisionMeshA;
        target.Clear(false);
        target.SetVertices(data.colliderVertices);
        target.SetTriangles(data.colliderTriangles, 0, false);
        SetKnownChunkBounds(target);
        lastPreparedCollisionVertexCount = data.colliderVertices.Count;
        lastPreparedCollisionTriangleCount = data.colliderTriangles.Count;
        preparedCollisionMesh = target;
        collisionBuildId++;
        return new CollisionPreparation(
            target.GetInstanceID(),
            LeaseId,
            collisionBuildId,
            contentVersion);
    }

    private void EnsureCollisionMeshes()
    {
        collisionMeshA ??= CreateCollisionMesh("Chunk Collision A");
        collisionMeshB ??= CreateCollisionMesh("Chunk Collision B");
    }

    private void ApplyRenderMesh(MeshData data)
    {
        renderMesh.Clear(false);
        renderMesh.subMeshCount = 1;
        renderMesh.SetVertices(data.vertices);
        if (data.normals.Count == data.vertices.Count)
            renderMesh.SetNormals(data.normals);
        renderMesh.SetUVs(0, data.uvs);
        if (data.tiledUvs.Count == data.vertices.Count)
            renderMesh.SetUVs(1, data.tiledUvs);
        if (data.colors.Count == data.vertices.Count)
            renderMesh.SetColors(data.colors);
        renderMesh.SetTriangles(data.triangles, 0, false);
        SetKnownChunkBounds(renderMesh);
    }

    private void SetKnownChunkBounds(Mesh mesh)
    {
        if (ChunkData == null)
            return;
        float scale = VoxelMetrics.Size;
        mesh.bounds = new Bounds(
            new Vector3(
                (ChunkData.chunkSize - 1) * scale * 0.5f,
                (ChunkData.chunkHeight - 1) * scale * 0.5f,
                (ChunkData.chunkSize - 1) * scale * 0.5f),
            new Vector3(
                ChunkData.chunkSize * scale,
                ChunkData.chunkHeight * scale,
                ChunkData.chunkSize * scale));
    }

    private void SetFarRenderMode(bool far)
    {
        if (meshRenderer == null)
            return;
        // All LODs share one render state. Voxel terrain still casts shadows onto
        // dynamic objects, but does not receive the camera-bounded shadow atlas;
        // that atlas cutoff was the visible dark ring around each active camera.
        meshRenderer.shadowCastingMode = ShadowCastingMode.On;
        meshRenderer.receiveShadows = false;
    }

    public bool IsCollisionPreparationCurrent(CollisionPreparation preparation)
    {
        return preparation.IsValid &&
               preparation.leaseId == LeaseId &&
               preparation.buildId == collisionBuildId &&
               preparation.contentVersion == AppliedContentVersion &&
               preparedCollisionMesh != null &&
               preparedCollisionMesh.GetInstanceID() == preparation.meshInstanceId;
    }

    public void DiscardCollisionPreparation(CollisionPreparation preparation)
    {
        if (!IsCollisionPreparationCurrent(preparation))
            return;
        preparedCollisionMesh = null;
        collisionBuildId++;
    }

    public bool TryActivateBakedCollision(int leaseId, int buildId, int contentVersion)
    {
        if (leaseId != LeaseId || buildId != collisionBuildId ||
            contentVersion != AppliedContentVersion || !collisionRequested ||
            preparedCollisionMesh == null)
        {
            lastCollisionActivationFailure =
                $"stale preparation lease={leaseId}/{LeaseId}, build={buildId}/{collisionBuildId}, " +
                $"content={contentVersion}/{AppliedContentVersion}, requested={collisionRequested}, " +
                $"prepared={preparedCollisionMesh != null}";
            return false;
        }

        // Cook explicitly while still on the main thread. Relying on assignment
        // alone becomes unreliable after a MeshCollider and its two reusable
        // meshes have crossed several pool leases: Unity can reject sharedMesh
        // and return null even though the geometry is valid. Explicit cooking
        // refreshes Unity's cache for the reused mesh instance deterministically.
#pragma warning disable CS0618 // Unity 6.3 keeps the int overload for Mesh instance IDs.
        Physics.BakeMesh(
            preparedCollisionMesh.GetInstanceID(),
            false,
            meshCollider.cookingOptions);
#pragma warning restore CS0618

        meshCollider.enabled = false;
        activeCollisionMesh = preparedCollisionMesh;
        preparedCollisionMesh = null;
        activeCollisionContentVersion = contentVersion;
        collisionResolvedContentVersion = contentVersion;
        meshCollider.sharedMesh = activeCollisionMesh;
        if (meshCollider.sharedMesh == null)
        {
            lastCollisionActivationFailure =
                $"MeshCollider rejected mesh vertices={lastPreparedCollisionVertexCount}, " +
                $"triangleIndices={lastPreparedCollisionTriangleCount}";
            ClearCollisionCache();
            collisionRequested = true;
            return false;
        }
        meshCollider.enabled = true;
        lastCollisionActivationFailure = null;
        return true;
    }

    public bool TryReactivateCollision(int contentVersion)
    {
        if (activeCollisionMesh == null || activeCollisionContentVersion != contentVersion ||
            ChunkData == null || ChunkData.ContentVersion != contentVersion)
            return false;

        collisionRequested = true;
        collisionResolvedContentVersion = contentVersion;
        if (meshCollider.sharedMesh != activeCollisionMesh)
            meshCollider.sharedMesh = activeCollisionMesh;
        if (meshCollider.sharedMesh == null)
        {
            ClearCollisionCache();
            collisionRequested = true;
            return false;
        }
        meshCollider.enabled = true;
        return true;
    }

    public void SetCollisionActive(bool active)
    {
        collisionRequested = active;
        if (!active)
        {
            if (preparedCollisionMesh != null)
            {
                preparedCollisionMesh = null;
                collisionBuildId++;
            }
            meshCollider.enabled = false;
            // Keep the cooked mesh and version while this renderer remains leased
            // to the same chunk. Backtracking can then restore collision without
            // waiting for another worker build and Physics.BakeMesh call. Keeping
            // sharedMesh assigned also prevents Unity from dropping the cooked
            // association while the collider is temporarily disabled.
        }
        else if (activeCollisionMesh != null)
        {
            meshCollider.sharedMesh = activeCollisionMesh;
            meshCollider.enabled = true;
        }
    }

    private void ClearCollisionCache()
    {
        if (preparedCollisionMesh != null)
        {
            preparedCollisionMesh = null;
            collisionBuildId++;
        }
        meshCollider.enabled = false;
        meshCollider.sharedMesh = null;
        activeCollisionMesh = null;
        activeCollisionContentVersion = -1;
        collisionResolvedContentVersion = -1;
    }

    public void ResetForPool()
    {
        LeaseId++;
        collisionRequested = false;
        collisionBuildId++;
        meshCollider.enabled = false;
        meshCollider.sharedMesh = null;
        preparedCollisionMesh = null;
        activeCollisionMesh = null;
        activeCollisionContentVersion = -1;
        collisionResolvedContentVersion = -1;
        lastCollisionActivationFailure = null;
        lastPreparedCollisionVertexCount = 0;
        lastPreparedCollisionTriangleCount = 0;
        ChunkData = null;
        ActiveLod = ChunkLodLevel.Full;
        AppliedContentVersion = -1;
        renderMesh.Clear(false);
        // PhysX caches cooked data by the native Mesh identity. A collision mesh
        // that is cleared, moved to another chunk through the renderer pool and
        // filled again can subsequently be rejected by MeshCollider.sharedMesh.
        // Keep meshes while backtracking within the same lease, but give a pooled
        // renderer fresh collision mesh identities for its next chunk.
        ReleaseCollisionMeshes();
        SetFarRenderMode(false);
    }

    private void ReleaseCollisionMeshes()
    {
        Mesh first = collisionMeshA;
        Mesh second = collisionMeshB;
        collisionMeshA = null;
        collisionMeshB = null;
        ReleaseMesh(first);
        ReleaseMesh(second);
    }

    private static void ReleaseMesh(Mesh mesh)
    {
        if (mesh == null)
            return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(mesh);
        else
            Destroy(mesh);
#else
        Destroy(mesh);
#endif
    }

    // Compatibility entry points. Streaming edits normally enqueue a worker rebuild.
    public void UpdateChunk()
    {
        if (ChunkData == null)
            return;
        MeshData data = DistanceSimplifier.BuildMesh(ChunkData, ActiveLod);
        ApplyMesh(data, ActiveLod, ChunkData.ContentVersion, false);
    }

    public void UpdateChunk(MeshData data)
    {
        ApplyMesh(data, ChunkLodLevel.Full, ChunkData != null ? ChunkData.ContentVersion : -1, false);
    }

    public void UpdateChunk(MeshData data, ChunkLodLevel lod, int contentVersion)
    {
        ApplyMesh(data, lod, contentVersion, false);
    }

    private void OnDestroy()
    {
        ReleaseMesh(renderMesh);
        renderMesh = null;
        ReleaseCollisionMeshes();
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showGizmo || !Application.isPlaying || ChunkData == null)
            return;
        Gizmos.color = Selection.activeObject == gameObject
            ? new Color(0, 1, 0, 0.4f)
            : new Color(1, 0, 1, 0.4f);
        float s = VoxelMetrics.Size;
        Vector3 ext = new Vector3(ChunkData.chunkSize * s, ChunkData.chunkHeight * s, ChunkData.chunkSize * s);
        Gizmos.DrawCube(transform.position + (ext - Vector3.one * s) * 0.5f, ext);
    }
#endif
}
