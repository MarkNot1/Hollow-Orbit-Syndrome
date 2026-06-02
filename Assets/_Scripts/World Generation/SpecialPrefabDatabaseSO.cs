using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SpecialPrefabDatabase", menuName = "Data/SpecialPrefabDatabase")]
public class SpecialPrefabDatabaseSO : ScriptableObject
{
    public List<SpecialPrefabData> specialPrefabDataList = new List<SpecialPrefabData>();

    public IReadOnlyList<SpecialPrefabData> SpecialPrefabs => specialPrefabDataList;

    public bool TryGetPrefab(string itemId, out SpecialPrefabData prefabData)
    {
        prefabData = null;
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        for (int i = 0; i < specialPrefabDataList.Count; i++)
        {
            SpecialPrefabData candidate = specialPrefabDataList[i];
            if (candidate == null)
                continue;

            if (string.Equals(candidate.ItemId, itemId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.DisplayName, itemId, StringComparison.OrdinalIgnoreCase))
            {
                prefabData = candidate;
                return true;
            }
        }

        return false;
    }
}

[Serializable]
public class SpecialPrefabData
{
    [SerializeField]
    private string itemId;
    [SerializeField]
    private string displayName;
    [SerializeField]
    private GameObject prefab;
    [SerializeField]
    private Vector3Int sizeInVoxels = Vector3Int.one;
    [SerializeField]
    private SpecialPrefabInteractionType interactionType = SpecialPrefabInteractionType.Inspect;

    public string ItemId => itemId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? itemId : displayName;
    public GameObject Prefab => prefab;
    public Vector3Int SizeInVoxels => new Vector3Int(
        Mathf.Max(1, sizeInVoxels.x),
        Mathf.Max(1, sizeInVoxels.y),
        Mathf.Max(1, sizeInVoxels.z));
    public SpecialPrefabInteractionType InteractionType => interactionType;
}

public enum SpecialPrefabInteractionType
{
    Activate,
    Inspect
}
