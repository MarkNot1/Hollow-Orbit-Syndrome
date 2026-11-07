using UnityEngine;

[CreateAssetMenu(fileName = "Item", menuName = "New Item")]
public class ItemSO : ScriptableObject
{
    public string itemName;
    public Sprite itemIcon;
    public int maxStackSize;
    public GameObject itemPrefab;
    public GameObject handItemPrefab;
    public string description;

    [Header("Voxel placement")]
    public ItemPlacementKind placementKind;
    public VoxelType voxelType;
    public string specialPrefabId;
    public TextAsset structureBlueprint;
    public bool unlimited;
}

public enum ItemPlacementKind
{
    None,
    TerrainVoxel,
    SpecialPrefab,
    StructureBlueprint
}
