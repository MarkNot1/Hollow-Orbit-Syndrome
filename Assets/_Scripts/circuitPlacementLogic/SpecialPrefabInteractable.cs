using UnityEngine;

public class SpecialPrefabInteractable : MonoBehaviour, IInspectableInteractable
{
    [SerializeField]
    private string itemId;
    [SerializeField]
    private string displayName;
    [SerializeField]
    private Vector3Int attachedVoxelPosition;
    [SerializeField]
    private Vector3Int surfaceNormal = Vector3Int.forward;
    [SerializeField]
    private Vector3Int sizeInVoxels = Vector3Int.one;
    [SerializeField]
    private SpecialPrefabInteractionType interactionType = SpecialPrefabInteractionType.Inspect;

    public string ItemId => itemId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? itemId : displayName;
    public Vector3Int AttachedVoxelPosition => attachedVoxelPosition;
    public Vector3Int SurfaceNormal => surfaceNormal;
    public Vector3Int SizeInVoxels => sizeInVoxels;
    public SpecialPrefabInteractionType InteractionType => interactionType;
    public string InteractionPrompt => interactionType == SpecialPrefabInteractionType.Inspect ? "Inspect" : "Use";

    public void Configure(string id, string label, Vector3Int attachedVoxel, Vector3Int normal, Vector3Int voxelSize, SpecialPrefabInteractionType type)
    {
        itemId = id;
        displayName = label;
        attachedVoxelPosition = attachedVoxel;
        surfaceNormal = normal == Vector3Int.zero ? Vector3Int.forward : normal;
        sizeInVoxels = new Vector3Int(
            Mathf.Max(1, voxelSize.x),
            Mathf.Max(1, voxelSize.y),
            Mathf.Max(1, voxelSize.z));
        interactionType = type;
    }

    public virtual bool TryActivate(GameObject interactor)
    {
        return false;
    }

    public Bounds GetWorldBounds()
    {
        Transform tileGrid = transform.Find("TileGrid");
        if (tileGrid != null)
        {
            Renderer tileRenderer = tileGrid.GetComponent<Renderer>();
            if (tileRenderer != null)
                return tileRenderer.bounds;

            Collider tileCollider = tileGrid.GetComponent<Collider>();
            if (tileCollider != null)
                return tileCollider.bounds;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] is ParticleSystemRenderer)
                continue;

            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        if (hasBounds)
            return bounds;

        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds colliderBounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
                colliderBounds.Encapsulate(colliders[i].bounds);
            return colliderBounds;
        }

        return new Bounds(transform.position, Vector3.one * VoxelMetrics.Size);
    }

    public Vector3 GetPreferredViewDirection()
    {
        Vector3 normal = (Vector3)surfaceNormal;
        if (normal.sqrMagnitude < 0.01f)
            normal = transform.forward;

        return normal.normalized;
    }

    public Vector3 GetExitDirection(Vector3 playerPosition)
    {
        Vector3 normal = GetPreferredViewDirection();
        Vector3 horizontalNormal = Vector3.ProjectOnPlane(normal, Vector3.up);
        if (horizontalNormal.sqrMagnitude > 0.01f)
            return horizontalNormal.normalized;

        Vector3 awayFromInteractable = Vector3.ProjectOnPlane(playerPosition - GetWorldBounds().center, Vector3.up);
        if (awayFromInteractable.sqrMagnitude > 0.01f)
            return awayFromInteractable.normalized;

        return -transform.forward;
    }
}
