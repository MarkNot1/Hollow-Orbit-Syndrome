using System;
using UnityEngine;

public class CircuitInspectionController : InteractionInspectionController
{
    [SerializeField]
    private placementSystem placementSystem;

    protected override void HandleInspectionEntered(IInspectableInteractable interactable)
    {
        if (!TryGetCircuit(interactable, out SpecialPrefabInteractable circuit))
            return;

        EnsurePlacementSystem();
        if (placementSystem != null)
            placementSystem.SetActiveCircuit(circuit);
    }

    protected override void HandleInspectionExited(IInspectableInteractable interactable)
    {
        if (!TryGetCircuit(interactable, out _))
            return;

        EnsurePlacementSystem();
        if (placementSystem != null)
            placementSystem.ClearActiveCircuit();
    }

    private void EnsurePlacementSystem()
    {
        if (placementSystem == null)
            placementSystem = FindFirstObjectByType<placementSystem>();
    }

    private static bool TryGetCircuit(IInspectableInteractable interactable, out SpecialPrefabInteractable circuit)
    {
        circuit = interactable as SpecialPrefabInteractable;
        if (circuit == null)
            return false;

        return circuit is CircuitInteractable
            || string.Equals(circuit.ItemId, "Circuit", StringComparison.OrdinalIgnoreCase);
    }
}
