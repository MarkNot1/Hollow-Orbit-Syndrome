using UnityEngine;

public class CircuitInteractable : SpecialPrefabInteractable
{
    public void Configure(Vector3Int attachedVoxel, Vector3Int normal)
    {
        Configure(
            "Circuit",
            "Circuit",
            attachedVoxel,
            normal,
            Vector3Int.one,
            SpecialPrefabInteractionType.Inspect);
    }
}
