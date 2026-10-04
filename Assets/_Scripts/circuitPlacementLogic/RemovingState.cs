public sealed class RemovingState : IPlacementState
{
    private readonly ObjectPlacer objectPlacer;
    private readonly placementInputManager inputManager;
    private readonly CircuitWireSystem wireSystem;

    public RemovingState(
        ObjectPlacer objectPlacer,
        placementInputManager inputManager,
        CircuitWireSystem wireSystem)
    {
        this.objectPlacer = objectPlacer;
        this.inputManager = inputManager;
        this.wireSystem = wireSystem;
    }

    public bool Enter()
    {
        wireSystem?.EndRemovalPreview();
        return objectPlacer != null && objectPlacer.BeginRemoval();
    }

    public void UpdateState()
    {
        objectPlacer.UpdateRemovalPreview(inputManager);
        wireSystem?.UpdateRemovalPreview(inputManager);
    }

    public void HandlePrimaryAction()
    {
        if (!objectPlacer.TryRemoveSelectedComponent(inputManager))
            wireSystem?.TryRemoveSelectedWire(inputManager);
    }

    public void Exit()
    {
        objectPlacer.EndRemoval();
        wireSystem?.EndRemovalPreview();
    }
}
