using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerPlacementController : MonoBehaviour
{
    [SerializeField]
    private Camera mainCamera;
    [SerializeField]
    private World world;
    [SerializeField]
    private VoxelDataSO terrainVoxelDatabase;
    [SerializeField]
    private SpecialPrefabDatabaseSO specialPrefabDatabase;
    [SerializeField]
    private string selectedItemId = "Circuit";
    [SerializeField]
    private float interactionRayLength = 5f;

    private static readonly RaycastHit[] RaycastHitsNonAlloc = new RaycastHit[32];

    public string SelectedItemId
    {
        get => selectedItemId;
        set => selectedItemId = value;
    }

    public float InteractionRayLength
    {
        get => interactionRayLength;
        set => interactionRayLength = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;
        if (world == null)
            world = FindFirstObjectByType<World>();
#if UNITY_EDITOR
        if (specialPrefabDatabase == null)
            specialPrefabDatabase = AssetDatabase.LoadAssetAtPath<SpecialPrefabDatabaseSO>("Assets/_Scripts/World Generation/SpecialPrefabDatabase.asset");
#endif
    }

    public bool TryPlaceSelectedItem()
    {
        if (world == null || mainCamera == null)
            return false;

        if (!TryGetChunkHit(out RaycastHit hit))
            return false;

        if (TryResolveTerrainVoxel(selectedItemId, out VoxelType voxelType))
            return world.PlaceVoxel(hit, voxelType);

        if (specialPrefabDatabase != null && specialPrefabDatabase.TryGetPrefab(selectedItemId, out SpecialPrefabData specialPrefab))
            return world.PlaceSpecialPrefab(hit, specialPrefab);

        if (world.PlaceSpecialPrefab(hit, selectedItemId))
            return true;

        if (IsCircuitFallback(selectedItemId))
            return world.PlaceSpecialVoxel(hit);

        Debug.LogWarning("PlayerPlacementController: No terrain voxel or special prefab exists for selected item '" + selectedItemId + "'.", this);
        return false;
    }

    private bool TryGetChunkHit(out RaycastHit bestHit)
    {
        bestHit = default;
        Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);
        int hitCount = Physics.RaycastNonAlloc(ray, RaycastHitsNonAlloc, interactionRayLength, ~0, QueryTriggerInteraction.Ignore);
        if (hitCount <= 0)
            return false;

        float bestDistance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = RaycastHitsNonAlloc[i];
            if (hit.collider != null && hit.collider.GetComponent<ChunkRenderer>() != null && hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }
        }

        return found;
    }

    private bool TryResolveTerrainVoxel(string itemId, out VoxelType voxelType)
    {
        voxelType = VoxelType.Nothing;
        if (string.IsNullOrWhiteSpace(itemId) || !System.Enum.TryParse(itemId, true, out voxelType))
            return false;

        if (terrainVoxelDatabase != null)
            return TerrainDatabaseContains(voxelType);

        if (VoxelDataManager.voxelTextureDataDictionary.Count > 0)
            return VoxelDataManager.voxelTextureDataDictionary.ContainsKey(voxelType);

        return voxelType != VoxelType.Nothing && voxelType != VoxelType.Air;
    }

    private bool TerrainDatabaseContains(VoxelType voxelType)
    {
        if (terrainVoxelDatabase.textureDataList == null)
            return false;

        for (int i = 0; i < terrainVoxelDatabase.textureDataList.Count; i++)
        {
            TextureData data = terrainVoxelDatabase.textureDataList[i];
            if (data != null && data.voxelType == voxelType)
                return true;
        }

        return false;
    }

    private static bool IsCircuitFallback(string itemId)
    {
        return string.Equals(itemId, "Circuit", System.StringComparison.OrdinalIgnoreCase);
    }
}
