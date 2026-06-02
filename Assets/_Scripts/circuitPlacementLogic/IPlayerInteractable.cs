using UnityEngine;

public interface IPlayerInteractable
{
    SpecialPrefabInteractionType InteractionType { get; }
    string InteractionPrompt { get; }
    bool TryActivate(GameObject interactor);
}
