using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class VoxelStructure : MonoBehaviour
{
    internal static readonly Vector3Int[] FaceNormals =
        { Vector3Int.left, Vector3Int.right, Vector3Int.down, Vector3Int.up, Vector3Int.back, Vector3Int.forward };
    internal static readonly Direction[] FaceDirections =
        { Direction.left, Direction.right, Direction.down, Direction.up, Direction.backwards, Direction.forward };

    private readonly Dictionary<Vector3Int, StructureChunk> chunks = new();
    private readonly HashSet<StructureChunk> dirtyChunks = new();
    private readonly List<StructureAttachment> attachments = new();
    private Material material;
    private SpecialPrefabDatabaseSO prefabDatabase;
    public string Id { get; private set; } = Guid.NewGuid().ToString("N");
    public bool HasMasterFrame { get; private set; }
    public int VoxelCount => chunks.Values.Sum(chunk => chunk.Voxels.Count);
    public IReadOnlyList<StructureAttachment> Attachments => attachments;

    public void Configure(Material voxelMaterial, SpecialPrefabDatabaseSO prefabs)
    {
        material = voxelMaterial;
        prefabDatabase = prefabs;
    }

    public VoxelType GetVoxel(Vector3Int position)
    {
        Vector3Int origin = ChunkOrigin(position);
        return chunks.TryGetValue(origin, out StructureChunk chunk) &&
               chunk.Voxels.TryGetValue(position - origin, out VoxelType type) ? type : VoxelType.Air;
    }

    public bool SetVoxel(Vector3Int position, VoxelType type)
    {
        if (IsProtectedVoxel(position)) return false;
        if (type != VoxelType.Air && !VoxelRuntimeData.IsSolid(type)) return false;
        VoxelType previous = GetVoxel(position);
        if (previous == type) return false;
        Vector3Int origin = ChunkOrigin(position);
        if (!chunks.TryGetValue(origin, out StructureChunk chunk))
        {
            GameObject child = new($"Voxels {origin}");
            child.transform.SetParent(transform, false);
            chunk = child.AddComponent<StructureChunk>();
            chunk.Initialize(this, origin, material);
            chunks.Add(origin, chunk);
        }
        if (type == VoxelType.Air)
        {
            chunk.Voxels.Remove(position - origin);
            for (int i = attachments.Count - 1; i >= 0; i--)
                if (attachments[i].Data.surfaceVoxel == position) RemoveAttachment(attachments[i]);
        }
        else chunk.Voxels[position - origin] = type;
        dirtyChunks.Add(chunk);
        foreach (Vector3Int normal in FaceNormals)
            if (chunks.TryGetValue(ChunkOrigin(position + normal), out StructureChunk neighbour))
                dirtyChunks.Add(neighbour);
        return true;
    }

    public bool IsProtectedVoxel(Vector3Int position) => HasMasterFrame && FrameBlueprint.ContainsEdge(position);

    internal void ProtectMasterFrame() => HasMasterFrame = true;

    public bool TryGetHitCell(RaycastHit hit, out Vector3Int surface, out Vector3Int normal)
    {
        surface = normal = default;
        StructureChunk chunk = hit.collider != null ? hit.collider.GetComponent<StructureChunk>() : null;
        if (chunk == null || chunk.Structure != this) return false;
        Vector3 localNormal = transform.InverseTransformDirection(hit.normal);
        normal = VoxelMetrics.FaceNormal(localNormal);
        surface = Vector3Int.RoundToInt(transform.InverseTransformPoint(hit.point) / VoxelMetrics.Size - localNormal * 0.01f);
        return normal != Vector3Int.zero && GetVoxel(surface) != VoxelType.Air;
    }

    public bool TryPlaceVoxel(RaycastHit hit, VoxelType type)
    {
        if (!VoxelRuntimeData.IsSolid(type) || !TryGetHitCell(hit, out var surface, out var normal)) return false;
        Physics.SyncTransforms();
        Vector3Int destination = surface + normal;
        return CanOccupy(destination) && SetVoxel(destination, type);
    }

    public bool CanOccupy(Vector3Int position)
    {
        return GetVoxel(position) == VoxelType.Air && StructureSystem.IsSpaceFree(
            transform.TransformPoint((Vector3)position * VoxelMetrics.Size), transform.rotation,
            Vector3.one * (VoxelMetrics.Size * 0.5f - 0.005f), this);
    }

    public bool TryPlacePrefab(RaycastHit hit, string prefabId)
    {
        Physics.SyncTransforms();
        if (!TryGetHitCell(hit, out var surface, out var normal) || !CanOccupy(surface + normal) ||
            prefabDatabase == null || !prefabDatabase.TryGetPrefab(prefabId, out SpecialPrefabData prefab)) return false;
        foreach (StructureAttachment existing in attachments)
            if (existing.Data.surfaceVoxel == surface && existing.Data.normal == normal) return false;
        RebuildMeshes();
        GameObject instance = SpecialVoxelManager.CreateInstance(prefab, transform, surface, normal);
        if (instance == null) return false;
        Bounds bounds = SpecialVoxelManager.GetLocalRenderBounds(instance);
        Physics.SyncTransforms();
        Vector3 halfSize = Vector3.Max(Vector3.one * 0.001f,
            Vector3.Scale(bounds.extents, instance.transform.localScale) - Vector3.one * 0.005f);
        if (!StructureSystem.IsSpaceFree(instance.transform.TransformPoint(bounds.center), instance.transform.rotation,
                halfSize, null, instance.transform))
        {
            instance.SetActive(false);
            Release(instance);
            return false;
        }
        StructureAttachment attachment = instance.AddComponent<StructureAttachment>();
        attachment.Data = new StructurePrefabData { id = Guid.NewGuid().ToString("N"),
            prefabId = prefab.ItemId, surfaceVoxel = surface, normal = normal };
        attachments.Add(attachment);
        return true;
    }

    public bool RemoveAttachment(StructureAttachment attachment)
    {
        if (attachment == null || !attachments.Remove(attachment)) return false;
        attachment.gameObject.SetActive(false);
        Release(attachment.gameObject);
        return true;
    }

    public StructureData Capture()
    {
        StructureData data = new() { id = Id, name = name, hasMasterFrame = HasMasterFrame,
            position = transform.position, rotation = transform.rotation };
        foreach (var chunk in chunks.Values)
            foreach (var voxel in chunk.Voxels)
                data.voxels.Add(new StructureVoxelData(chunk.Origin + voxel.Key, voxel.Value));
        data.voxels.Sort((a, b) => ComparePosition(a.position, b.position));
        foreach (StructureAttachment attachment in attachments) data.prefabs.Add(attachment.Capture());
        return data;
    }

    public void Restore(StructureData data)
    {
        if (chunks.Count != 0 || attachments.Count != 0) throw new InvalidOperationException("Restore requires an empty structure.");
        StructureSystem.Validate(data, prefabDatabase);
        Id = string.IsNullOrWhiteSpace(data.id) ? Guid.NewGuid().ToString("N") : data.id;
        name = data.name;
        transform.SetPositionAndRotation(data.position, data.rotation.normalized);
        foreach (var voxel in data.voxels) SetVoxel(voxel.position, voxel.type);
        HasMasterFrame = data.hasMasterFrame;
        RebuildMeshes();
        foreach (StructurePrefabData record in data.prefabs)
        {
            prefabDatabase.TryGetPrefab(record.prefabId, out SpecialPrefabData prefab);
            GameObject instance = SpecialVoxelManager.CreateInstance(prefab, transform, record.surfaceVoxel, record.normal);
            instance.transform.SetLocalPositionAndRotation(record.position, record.rotation.normalized);
            instance.transform.localScale = record.scale;
            StructureAttachment attachment = instance.AddComponent<StructureAttachment>();
            attachment.Data = record;
            attachments.Add(attachment);
            if (!string.IsNullOrEmpty(record.state))
            {
                IStructurePrefabState state = instance.GetComponent<IStructurePrefabState>();
                if (state == null) throw new InvalidOperationException($"Prefab '{record.prefabId}' cannot restore its saved state.");
                state.RestoreState(record.state);
            }
        }
    }

    private void LateUpdate() => RebuildMeshes();

    public void RebuildMeshes()
    {
        foreach (StructureChunk chunk in dirtyChunks)
        {
            if (chunk.Voxels.Count == 0)
            {
                chunks.Remove(chunk.Origin);
                chunk.gameObject.SetActive(false);
                Release(chunk.gameObject);
            }
            else chunk.Rebuild();
        }
        dirtyChunks.Clear();
    }

    internal static Vector3Int ChunkOrigin(Vector3Int p) => new(
        Mathf.FloorToInt((float)p.x / StructureChunk.Size) * StructureChunk.Size,
        Mathf.FloorToInt((float)p.y / StructureChunk.Size) * StructureChunk.Size,
        Mathf.FloorToInt((float)p.z / StructureChunk.Size) * StructureChunk.Size);

    private static int ComparePosition(Vector3Int a, Vector3Int b)
    {
        int x = a.x.CompareTo(b.x);
        if (x != 0) return x;
        int y = a.y.CompareTo(b.y);
        return y != 0 ? y : a.z.CompareTo(b.z);
    }

    public static void Release(UnityEngine.Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
