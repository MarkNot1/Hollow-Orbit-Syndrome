using UnityEngine;

public sealed class StructureAttachment : MonoBehaviour
{
    public StructurePrefabData Data { get; internal set; }

    internal StructurePrefabData Capture()
    {
        return new StructurePrefabData
        {
            id = Data.id, prefabId = Data.prefabId,
            surfaceVoxel = Data.surfaceVoxel, normal = Data.normal,
            position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale,
            state = GetComponent<IStructurePrefabState>()?.CaptureState()
        };
    }
}
