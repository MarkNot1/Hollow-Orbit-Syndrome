using UnityEngine;

public interface IInspectableInteractable : IPlayerInteractable
{
    Bounds GetWorldBounds();
    Vector3 GetPreferredViewDirection();
    Vector3 GetExitDirection(Vector3 playerPosition);
}
