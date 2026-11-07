using UnityEngine;

/// <summary>
/// One voxel = <see cref="Size"/> meters per axis. Integer voxel/chunk keys are unchanged; only world-space placement scales.
/// </summary>
public static class VoxelMetrics
{
    public const float Size = 0.5f;

    public static Vector3 ChunkKeyToWorldOrigin(Vector3Int chunkKey)
    {
        return new Vector3(chunkKey.x, chunkKey.y, chunkKey.z) * Size;
    }

    /// <summary>
    /// Global voxel cell index for a world position (same grid as mesh: centers at integer × <see cref="Size"/>).
    /// </summary>
    public static Vector3Int WorldToVoxelCoord(Vector3 worldPosition)
    {
        return WorldPointToVoxelIndex(worldPosition);
    }

    /// <summary>
    /// Integer voxel index for the cell whose center-aligned AABB contains this world point (matches mesh layout).
    /// </summary>
    public static Vector3Int WorldPointToVoxelIndex(Vector3 worldPosition)
    {
        float inv = 1f / Size;
        return new Vector3Int(
            Mathf.RoundToInt(worldPosition.x * inv),
            Mathf.RoundToInt(worldPosition.y * inv),
            Mathf.RoundToInt(worldPosition.z * inv));
    }

    /// <summary>
    /// Voxel index of the solid hit by a ray (nudge slightly along -normal so boundary hits resolve inside the voxel).
    /// </summary>
    public static Vector3Int WorldHitToTargetVoxelIndex(RaycastHit hit, float surfaceBiasMeters = 0.002f)
    {
        return WorldPointToVoxelIndex(hit.point - hit.normal * surfaceBiasMeters);
    }

    /// <summary>
    /// Voxel index one cell beyond the hit face, including smoothed terrain faces.
    /// </summary>
    public static Vector3Int WorldHitToAdjacentVoxelIndex(RaycastHit hit, float surfaceBiasMeters = 0.002f)
    {
        return WorldHitToTargetVoxelIndex(hit, surfaceBiasMeters) + FaceNormal(hit.normal);
    }

    // Smoothed terrain can produce diagonal normals. Placement still advances exactly one cell.
    public static Vector3Int FaceNormal(Vector3 normal)
    {
        Vector3 magnitude = new(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
        if (magnitude.sqrMagnitude < 0.0001f) return Vector3Int.zero;
        if (magnitude.y >= magnitude.x && magnitude.y >= magnitude.z)
            return normal.y >= 0f ? Vector3Int.up : Vector3Int.down;
        if (magnitude.x >= magnitude.z)
            return normal.x >= 0f ? Vector3Int.right : Vector3Int.left;
        return normal.z >= 0f ? Vector3Int.forward : Vector3Int.back;
    }
}
