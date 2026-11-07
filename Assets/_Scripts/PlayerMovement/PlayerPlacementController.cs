using UnityEngine;

[DefaultExecutionOrder(200)]
public class PlayerPlacementController : MonoBehaviour
{
    [SerializeField] private World world;
    [SerializeField] private Inventory inventory;
    private PlayerInput playerInput;
    private StructureSystem structures;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        if (world == null) world = FindFirstObjectByType<World>();
        if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
        structures = FindFirstObjectByType<StructureSystem>();
    }

    private void LateUpdate()
    {
        if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
        ItemSO item = inventory != null ? inventory.EquippedItem : null;
        if (structures != null && inventory != null && !inventory.IsOpen && playerInput != null &&
            !playerInput.IsGameplayInputBlocked && item != null && inventory.GetAmount(item) > 0 &&
            item.placementKind == ItemPlacementKind.StructureBlueprint && playerInput.TryGetReticleHit(out RaycastHit hit))
            structures.ShowBlueprintPreview(hit, item.structureBlueprint);
        else if (structures != null) structures.HideBlueprintPreview();
    }

    private void OnDisable()
    {
        if (structures != null) structures.HideBlueprintPreview();
    }

    public bool TryPlaceEquippedVoxel()
    {
        if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
        if (inventory == null || inventory.IsOpen || playerInput == null ||
            playerInput.IsGameplayInputBlocked) return false;
        ItemSO item = inventory.EquippedItem;
        if (item == null || inventory.GetAmount(item) <= 0 || item.placementKind == ItemPlacementKind.None ||
            !playerInput.TryGetReticleHit(out RaycastHit hit)) return false;

        VoxelStructure structure = hit.collider.GetComponent<StructureChunk>()?.Structure;
        bool terrain = hit.collider.GetComponent<ChunkRenderer>() != null && world != null;

        bool placed = item.placementKind switch
        {
            ItemPlacementKind.TerrainVoxel => structure != null
                ? structure.TryPlaceVoxel(hit, item.voxelType) : terrain && world.PlaceVoxel(hit, item.voxelType),
            ItemPlacementKind.SpecialPrefab => structure != null
                ? structure.TryPlacePrefab(hit, item.specialPrefabId) : terrain && world.PlaceSpecialPrefab(hit, item.specialPrefabId),
            ItemPlacementKind.StructureBlueprint => structures != null && structures.TryPlaceBlueprint(hit, item.structureBlueprint),
            _ => false
        };
        if (placed && !item.unlimited) inventory.RemoveItem(item, 1);
        return placed;
    }
}
