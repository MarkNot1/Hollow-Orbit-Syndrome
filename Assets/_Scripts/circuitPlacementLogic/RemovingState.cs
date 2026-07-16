public sealed class RemovingState : IPlacementState
{
    private readonly ObjectPlacer objectPlacer;
    private readonly placementInputManager inputManager;

    public RemovingState(ObjectPlacer objectPlacer, placementInputManager inputManager)
    {
        this.objectPlacer = objectPlacer;
        this.inputManager = inputManager;
    }

    public bool Enter()
        => objectPlacer != null && objectPlacer.BeginRemoval();

    public void UpdateState()
        => objectPlacer.UpdateRemovalPreview(inputManager);

    public void HandlePrimaryAction()
        => objectPlacer.TryRemoveSelectedComponent(inputManager);

    public void Exit()
        => objectPlacer.EndRemoval();
}
