using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class SpecialVoxelManager : MonoBehaviour
{
    [SerializeField]
    private GameObject circuitVoxelPrefab;
    [SerializeField]
    private SpecialPrefabDatabaseSO specialPrefabDatabase;

    private const string CircuitItemId = "Circuit";
    private const float SurfaceOffset = 0.002f;
    private readonly Dictionary<SpecialPrefabKey, SpecialPrefabPlacement> placedPrefabs = new Dictionary<SpecialPrefabKey, SpecialPrefabPlacement>();
    private readonly Dictionary<SpecialPrefabKey, GameObject> spawnedPrefabs = new Dictionary<SpecialPrefabKey, GameObject>();
    private Transform container;
    private World world;

    public void Configure(World worldReference, GameObject circuitPrefabOverride, SpecialPrefabDatabaseSO prefabDatabase)
    {
        world = worldReference;
        if (circuitPrefabOverride != null)
            circuitVoxelPrefab = circuitPrefabOverride;
        if (prefabDatabase != null)
            specialPrefabDatabase = prefabDatabase;
    }

    public bool PlaceSpecialPrefab(RaycastHit hit, SpecialPrefabData prefabData)
    {
        if (prefabData == null || prefabData.Prefab == null || !EnsureWorldReady())
            return false;

        return PlaceSpecialPrefab(
            hit,
            prefabData.ItemId,
            prefabData.DisplayName,
            prefabData.Prefab,
            prefabData.SizeInVoxels,
            prefabData.InteractionType);
    }

    public bool PlaceCircuit(RaycastHit hit)
    {
        if (specialPrefabDatabase != null && specialPrefabDatabase.TryGetPrefab(CircuitItemId, out SpecialPrefabData circuitData))
            return PlaceSpecialPrefab(hit, circuitData);

        if (!EnsureCircuitFallbackReady())
            return false;

        return PlaceSpecialPrefab(
            hit,
            CircuitItemId,
            "Circuit",
            circuitVoxelPrefab,
            Vector3Int.one,
            SpecialPrefabInteractionType.Inspect);
    }

    public void ShowSpecialPrefabsForChunk(Vector3Int chunkPosition)
    {
        if (!EnsureWorldReady() || placedPrefabs.Count == 0)
            return;

        foreach (SpecialPrefabKey key in placedPrefabs.Keys)
        {
            if (WorldDataHelper.ChunkPositionFromVoxelCoords(world, key.SurfaceVoxel) == chunkPosition)
                SpawnSpecialPrefab(key);
        }
    }

    public void HideSpecialPrefabsForChunk(Vector3Int chunkPosition)
    {
        if (!EnsureWorldReady() || spawnedPrefabs.Count == 0)
            return;

        List<SpecialPrefabKey> keysToHide = new List<SpecialPrefabKey>();
        foreach (var item in spawnedPrefabs)
        {
            if (WorldDataHelper.ChunkPositionFromVoxelCoords(world, item.Key.SurfaceVoxel) == chunkPosition)
                keysToHide.Add(item.Key);
        }

        for (int i = 0; i < keysToHide.Count; i++)
        {
            Destroy(spawnedPrefabs[keysToHide[i]]);
            spawnedPrefabs.Remove(keysToHide[i]);
        }
    }

    public void RemoveSpecialPrefabsAttachedTo(Vector3Int surfaceVoxel)
    {
        List<SpecialPrefabKey> keysToRemove = new List<SpecialPrefabKey>();
        foreach (SpecialPrefabKey key in placedPrefabs.Keys)
        {
            if (key.SurfaceVoxel == surfaceVoxel)
                keysToRemove.Add(key);
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            SpecialPrefabKey key = keysToRemove[i];
            placedPrefabs.Remove(key);
            if (spawnedPrefabs.TryGetValue(key, out GameObject instance))
            {
                Destroy(instance);
                spawnedPrefabs.Remove(key);
            }
        }
    }

    public void ShowCircuitsForChunk(Vector3Int chunkPosition)
    {
        ShowSpecialPrefabsForChunk(chunkPosition);
    }

    public void HideCircuitsForChunk(Vector3Int chunkPosition)
    {
        HideSpecialPrefabsForChunk(chunkPosition);
    }

    public void RemoveCircuitsAttachedTo(Vector3Int surfaceVoxel)
    {
        RemoveSpecialPrefabsAttachedTo(surfaceVoxel);
    }

    private bool PlaceSpecialPrefab(
        RaycastHit hit,
        string itemId,
        string displayName,
        GameObject prefab,
        Vector3Int sizeInVoxels,
        SpecialPrefabInteractionType interactionType)
    {
        if (prefab == null || string.IsNullOrWhiteSpace(itemId) || !EnsureWorldReady() ||
            !world.TryGetVoxelPlacementCell(hit, out _))
            return false;

        Vector3Int surfaceVoxel = VoxelMetrics.WorldHitToTargetVoxelIndex(hit);
        Vector3Int normal = RoundNormal(hit.normal);
        if (normal == Vector3Int.zero)
            return false;

        ChunkData chunkData = WorldDataHelper.GetChunkData(world, surfaceVoxel);
        if (chunkData == null)
            return false;

        Vector3Int localPos = Chunk.GetVoxelInChunkCoordinates(chunkData, surfaceVoxel);
        VoxelType surfaceType = Chunk.GetVoxelFromChunkCoordinates(chunkData, localPos);
        if (surfaceType == VoxelType.Air || surfaceType == VoxelType.Nothing)
            return false;

        SpecialPrefabKey key = new SpecialPrefabKey(itemId, surfaceVoxel, normal);
        if (placedPrefabs.ContainsKey(key))
            return false;

        placedPrefabs.Add(key, new SpecialPrefabPlacement(
            itemId,
            displayName,
            prefab,
            SanitizeSize(sizeInVoxels),
            interactionType));

        chunkData.modifiedByThePlayer = true;
        if (WorldDataHelper.GetChunk(world, chunkData.worldPosition) != null)
            SpawnSpecialPrefab(key);

        return true;
    }

    private void SpawnSpecialPrefab(SpecialPrefabKey key)
    {
        if (!EnsureWorldReady())
            return;
        if (spawnedPrefabs.ContainsKey(key))
            return;
        if (!placedPrefabs.TryGetValue(key, out SpecialPrefabPlacement placement) || placement.Prefab == null)
            return;

        EnsureContainer();
        Quaternion rotation = GetSurfaceRotation(key.Normal);
        Vector3 position = ((Vector3)key.SurfaceVoxel * VoxelMetrics.Size)
            + ((Vector3)key.Normal * (VoxelMetrics.Size * 0.5f + SurfaceOffset));
        GameObject instance = Instantiate(placement.Prefab, position, rotation, container);

        FitInstanceToVoxelFace(instance, placement.SizeInVoxels, key.Normal);
        EnsureCollider(instance);
        EnsureInteractable(instance, key, placement);
        spawnedPrefabs.Add(key, instance);
    }

    public static GameObject CreateInstance(SpecialPrefabData prefab, Transform parent, Vector3Int surface, Vector3Int normal)
    {
        if (prefab?.Prefab == null) return null;
        GameObject instance = Instantiate(prefab.Prefab, parent);
        instance.transform.localPosition = (Vector3)surface * VoxelMetrics.Size +
            (Vector3)normal * (VoxelMetrics.Size * 0.5f + SurfaceOffset);
        instance.transform.localRotation = GetSurfaceRotation(normal);
        FitInstanceToVoxelFace(instance, prefab.SizeInVoxels, normal);
        EnsureCollider(instance);
        SpecialPrefabInteractable interactable = instance.GetComponent<SpecialPrefabInteractable>() ??
            instance.AddComponent<SpecialPrefabInteractable>();
        interactable.Configure(prefab.ItemId, prefab.DisplayName, surface, normal, prefab.SizeInVoxels, prefab.InteractionType);
        if (prefab.ItemId == CircuitItemId && instance.GetComponent<CircuitStructureState>() == null)
            instance.AddComponent<CircuitStructureState>();
        return instance;
    }

    private bool EnsureWorldReady()
    {
        if (world == null)
            world = GetComponent<World>();

        return world != null;
    }

    private bool EnsureCircuitFallbackReady()
    {
        if (!EnsureWorldReady())
            return false;

#if UNITY_EDITOR
        if (circuitVoxelPrefab == null)
            circuitVoxelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/circuitVoxel.prefab");
        if (circuitVoxelPrefab == null)
            circuitVoxelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/specialVoxels/circuitVoxel_complete.obj");
#endif

        return circuitVoxelPrefab != null;
    }

    private void EnsureContainer()
    {
        if (container != null)
            return;

        container = new GameObject("Special Prefabs").transform;
        container.SetParent(transform, false);
    }

    private static Vector3Int SanitizeSize(Vector3Int size)
    {
        return new Vector3Int(
            Mathf.Max(1, size.x),
            Mathf.Max(1, size.y),
            Mathf.Max(1, size.z));
    }

    private static Vector3Int RoundNormal(Vector3 normal)
    {
        return VoxelMetrics.FaceNormal(normal);
    }

    private static Quaternion GetSurfaceRotation(Vector3Int normal)
    {
        Vector3 forward = normal;
        Vector3 up = normal.y == 0 ? Vector3.up : Vector3.forward;
        return Quaternion.LookRotation(forward, up) * Quaternion.Euler(0f, 180f, 0f);
    }

    private static void FitInstanceToVoxelFace(GameObject instance, Vector3Int sizeInVoxels, Vector3Int normal)
    {
        Bounds bounds = GetLocalRenderBounds(instance);
        if (bounds.size == Vector3.zero)
            return;

        Vector2 targetFaceSize = GetTargetFaceSize(sizeInVoxels, normal) * VoxelMetrics.Size;
        float currentWidth = Mathf.Max(bounds.size.x, 0.0001f);
        float currentHeight = Mathf.Max(bounds.size.y, 0.0001f);
        float scale = Mathf.Min(targetFaceSize.x / currentWidth, targetFaceSize.y / currentHeight);
        instance.transform.localScale = Vector3.one * scale;

        Bounds scaledBounds = GetLocalRenderBounds(instance);
        Vector3 localOffset = -scaledBounds.center;
        localOffset.z = -scaledBounds.max.z;
        instance.transform.position += instance.transform.rotation * Vector3.Scale(localOffset, instance.transform.localScale);
    }

    private static Vector2 GetTargetFaceSize(Vector3Int sizeInVoxels, Vector3Int normal)
    {
        if (Mathf.Abs(normal.y) > 0)
            return new Vector2(sizeInVoxels.x, sizeInVoxels.z);
        if (Mathf.Abs(normal.x) > 0)
            return new Vector2(sizeInVoxels.z, sizeInVoxels.y);

        return new Vector2(sizeInVoxels.x, sizeInVoxels.y);
    }

    public static Bounds GetLocalRenderBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.zero);

        Matrix4x4 worldToRoot = instance.transform.worldToLocalMatrix;
        Bounds bounds = TransformBounds(worldToRoot * renderers[0].localToWorldMatrix, renderers[0].localBounds);
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(TransformBounds(worldToRoot * renderers[i].localToWorldMatrix, renderers[i].localBounds));

        return bounds;
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
        Vector3 extents = bounds.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));

        extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

        return new Bounds(center, extents * 2f);
    }

    private static void EnsureCollider(GameObject instance)
    {
        if (instance.GetComponentInChildren<Collider>() != null)
            return;

        Bounds bounds = GetLocalRenderBounds(instance);
        BoxCollider collider = instance.AddComponent<BoxCollider>();
        collider.center = bounds.center;
        collider.size = bounds.size;
    }

    private static void EnsureInteractable(GameObject instance, SpecialPrefabKey key, SpecialPrefabPlacement placement)
    {
        SpecialPrefabInteractable interactable = instance.GetComponent<SpecialPrefabInteractable>();
        if (interactable == null)
            interactable = instance.AddComponent<SpecialPrefabInteractable>();

        interactable.Configure(
            placement.ItemId,
            placement.DisplayName,
            key.SurfaceVoxel,
            key.Normal,
            placement.SizeInVoxels,
            placement.InteractionType);
    }

    private readonly struct SpecialPrefabPlacement
    {
        public readonly string ItemId;
        public readonly string DisplayName;
        public readonly GameObject Prefab;
        public readonly Vector3Int SizeInVoxels;
        public readonly SpecialPrefabInteractionType InteractionType;

        public SpecialPrefabPlacement(string itemId, string displayName, GameObject prefab, Vector3Int sizeInVoxels, SpecialPrefabInteractionType interactionType)
        {
            ItemId = itemId;
            DisplayName = displayName;
            Prefab = prefab;
            SizeInVoxels = sizeInVoxels;
            InteractionType = interactionType;
        }
    }

    private readonly struct SpecialPrefabKey : IEquatable<SpecialPrefabKey>
    {
        public readonly string ItemId;
        public readonly Vector3Int SurfaceVoxel;
        public readonly Vector3Int Normal;

        public SpecialPrefabKey(string itemId, Vector3Int surfaceVoxel, Vector3Int normal)
        {
            ItemId = itemId;
            SurfaceVoxel = surfaceVoxel;
            Normal = normal;
        }

        public bool Equals(SpecialPrefabKey other)
        {
            return string.Equals(ItemId, other.ItemId, StringComparison.OrdinalIgnoreCase)
                && SurfaceVoxel == other.SurfaceVoxel
                && Normal == other.Normal;
        }

        public override bool Equals(object obj)
        {
            return obj is SpecialPrefabKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(ItemId ?? string.Empty);
                hash = (hash * 397) ^ SurfaceVoxel.GetHashCode();
                hash = (hash * 397) ^ Normal.GetHashCode();
                return hash;
            }
        }
    }
}
