using System;

public sealed class PlacementState : IPlacementState
{
    private readonly ObjectPlacer objectPlacer;
    private readonly placementInputManager inputManager;
    private readonly int componentId;
    private readonly Action onPlacementCompleted;

    public PlacementState(
        ObjectPlacer objectPlacer,
        placementInputManager inputManager,
        int componentId,
        Action onPlacementCompleted)
    {
        this.objectPlacer = objectPlacer;
        this.inputManager = inputManager;
        this.componentId = componentId;
        this.onPlacementCompleted = onPlacementCompleted;
    }

    public bool Enter()
        => objectPlacer != null && objectPlacer.BeginPlacement(componentId);

    public void UpdateState()
        => objectPlacer.UpdatePlacementPreview(inputManager);

    public void HandlePrimaryAction()
    {
        if (objectPlacer.TryPlaceSelectedComponent(inputManager))
            onPlacementCompleted?.Invoke();
    }

    public void HandleRotateAction()
        => objectPlacer.RotateSelectedComponent(inputManager);

    public void Exit()
        => objectPlacer.EndPlacement();
}
