using System;

public sealed class WiringState : IPlacementState
{
    private readonly CircuitWireSystem wireSystem;
    private readonly placementInputManager inputManager;
    private readonly CircuitNode sourceNode;
    private readonly Action onCompleted;

    public WiringState(
        CircuitWireSystem wireSystem,
        placementInputManager inputManager,
        CircuitNode sourceNode,
        Action onCompleted)
    {
        this.wireSystem = wireSystem;
        this.inputManager = inputManager;
        this.sourceNode = sourceNode;
        this.onCompleted = onCompleted;
    }

    public bool Enter()
        => wireSystem != null && wireSystem.BeginWiring(sourceNode);

    public void UpdateState()
        => wireSystem.UpdatePreview(inputManager);

    public void HandlePrimaryAction()
    {
        if (wireSystem.HandlePrimaryAction(inputManager))
            onCompleted?.Invoke();
    }

    public void Exit()
        => wireSystem.CancelWiring();
}
