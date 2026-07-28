using System;
using UnityEngine;
using UnityEngine.UI;

public class CircuitInspectionController : InteractionInspectionController
{
    [SerializeField]
    private placementSystem placementSystem;
    [SerializeField]
    private GameObject componentControlsRoot;
    [SerializeField]
    private string componentControlsRootName = "Panel";

    private void Start()
    {
        EnsureComponentControlsRoot();
        SetComponentControlsVisible(false);
    }

    protected override void HandleInspectionEntered(IInspectableInteractable interactable)
    {
        if (!TryGetCircuit(interactable, out SpecialPrefabInteractable circuit))
            return;

        EnsurePlacementSystem();
        if (placementSystem != null)
            placementSystem.SetActiveCircuit(circuit);

        EnsureComponentControlsRoot();
        SetComponentControlsVisible(true);
    }

    protected override void HandleInspectionExited(IInspectableInteractable interactable)
    {
        if (!TryGetCircuit(interactable, out _))
            return;

        EnsurePlacementSystem();
        if (placementSystem != null)
            placementSystem.ClearActiveCircuit();

        SetComponentControlsVisible(false);
    }

    protected override bool ShouldMovePlayerAfterInspection(IInspectableInteractable interactable)
        => !TryGetCircuit(interactable, out _);

    private void EnsurePlacementSystem()
    {
        if (placementSystem == null)
            placementSystem = FindFirstObjectByType<placementSystem>();
    }

    private void EnsureComponentControlsRoot()
    {
        if (componentControlsRoot != null)
            return;

        RectTransform[] rectTransforms = Resources.FindObjectsOfTypeAll<RectTransform>();
        for (int i = 0; i < rectTransforms.Length; i++)
        {
            GameObject candidate = rectTransforms[i].gameObject;
            if (!candidate.scene.IsValid() || candidate.name != componentControlsRootName)
                continue;

            if (!ContainsPlacementButton(candidate))
                continue;

            componentControlsRoot = candidate;
            return;
        }
    }

    private void SetComponentControlsVisible(bool visible)
    {
        if (componentControlsRoot != null && componentControlsRoot.activeSelf != visible)
            componentControlsRoot.SetActive(visible);
    }

    private static bool ContainsPlacementButton(GameObject root)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            int eventCount = buttons[i].onClick.GetPersistentEventCount();
            for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
            {
                if (buttons[i].onClick.GetPersistentMethodName(eventIndex) == nameof(placementSystem.StartPlacement))
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetCircuit(IInspectableInteractable interactable, out SpecialPrefabInteractable circuit)
    {
        circuit = interactable as SpecialPrefabInteractable;
        if (circuit == null)
            return false;

        return string.Equals(circuit.ItemId, "Circuit", StringComparison.OrdinalIgnoreCase);
    }
}
