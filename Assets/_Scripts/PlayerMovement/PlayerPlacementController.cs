using UnityEngine;

public class PlayerPlacementController : MonoBehaviour
{
    [SerializeField] private World world;
    [SerializeField] private Inventory inventory;
    private PlayerInput playerInput;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        if (world == null) world = FindFirstObjectByType<World>();
        if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
    }

    public bool TryPlaceEquippedVoxel()
    {
        if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
        if (inventory == null || inventory.IsOpen || world == null || playerInput == null ||
            playerInput.IsGameplayInputBlocked) return false;
        ItemSO item = inventory.EquippedItem;
        if (item == null || inventory.GetAmount(item) <= 0 || item.placementKind == ItemPlacementKind.None ||
            !playerInput.TryGetReticleHit(out RaycastHit hit) ||
            hit.collider.GetComponent<ChunkRenderer>() == null) return false;

        bool placed = item.placementKind switch
        {
            ItemPlacementKind.TerrainVoxel => world.PlaceVoxel(hit, item.voxelType),
            ItemPlacementKind.SpecialPrefab => world.PlaceSpecialPrefab(hit, item.specialPrefabId),
            _ => false
        };
        if (placed && !item.unlimited) inventory.RemoveItem(item, 1);
        return placed;
    }
}
