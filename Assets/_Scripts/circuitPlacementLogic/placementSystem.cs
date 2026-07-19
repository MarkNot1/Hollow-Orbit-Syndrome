using UnityEngine;

public class placementSystem : MonoBehaviour
{
    private const int CircuitGridSize = 16;
    private const string TileGridName = "TileGrid";
    private const float GridSurfaceOffset = 0.002f;
    private static readonly int GridSizeProperty = Shader.PropertyToID("_Size");
    private static readonly int GridOffsetProperty = Shader.PropertyToID("_GridOffset");

    [SerializeField]
    private placementInputManager inputManager;

    [SerializeField]
    private ObjectPlacer objectPlacer;

    [SerializeField]
    private CircuitWireSystem wireSystem;

    [SerializeField]
    private CircuitSimulationSystem simulationSystem;

    [SerializeField]
    private CircuitComponentHoverDisplay hoverDisplay;

    private SpecialPrefabInteractable activeCircuit;
    private IPlacementState activeState;
    private MaterialPropertyBlock tileGridPropertyBlock;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToInput();
    }

    private void OnDisable()
    {
        UnsubscribeFromInput();
        StopCurrentState();

        if (objectPlacer != null)
            objectPlacer.ClearActiveBoard();
        if (wireSystem != null)
            wireSystem.ClearActiveBoard();
        if (simulationSystem != null)
            simulationSystem.ClearActiveBoard();
    }

    private void Update()
    {
        activeState?.UpdateState();
    }

    public void SetActiveCircuit(SpecialPrefabInteractable circuit)
    {
        StopCurrentState();
        activeCircuit = circuit;

        if (objectPlacer == null)
        {
            Debug.LogError("ObjectPlacer is not assigned to placementSystem.", this);
            return;
        }

        if (!TryConfigureCircuitBoard(out CircuitBoardContext board))
        {
            objectPlacer.ClearActiveBoard();
            if (wireSystem != null)
                wireSystem.ClearActiveBoard();
            if (simulationSystem != null)
                simulationSystem.ClearActiveBoard();
            return;
        }

        objectPlacer.SetActiveBoard(board);
        if (wireSystem != null)
            wireSystem.SetActiveBoard(board, objectPlacer.ActiveGrid);
        if (simulationSystem != null)
            simulationSystem.SetActiveBoard(board);
    }

    public void ClearActiveCircuit()
    {
        StopCurrentState();
        activeCircuit = null;

        if (objectPlacer != null)
            objectPlacer.ClearActiveBoard();
        if (wireSystem != null)
            wireSystem.ClearActiveBoard();
        if (simulationSystem != null)
            simulationSystem.ClearActiveBoard();
    }

    public void StartPlacement(int ID)
    {
        if (activeCircuit == null)
        {
            Debug.LogWarning("Cannot place circuit components without inspecting a circuit.", this);
            return;
        }

        if (inputManager == null || objectPlacer == null)
        {
            Debug.LogError("Placement input manager or ObjectPlacer is not assigned.", this);
            return;
        }

        ChangeState(new PlacementState(
            objectPlacer,
            inputManager,
            ID,
            HandlePlacementCompleted));
    }

    private void HandlePrimaryAction()
    {
        if (activeState != null)
        {
            activeState.HandlePrimaryAction();
            return;
        }

        if (activeCircuit != null && wireSystem != null && inputManager != null &&
            wireSystem.TryGetNode(inputManager, out CircuitNode node))
        {
            ChangeState(new WiringState(
                wireSystem,
                inputManager,
                node,
                HandleWiringCompleted));
        }
    }

    private void ToggleRemovalState()
    {
        if (inputManager != null && inputManager.IsPointerOverUI())
            return;

        if (activeState is WiringState)
        {
            StopCurrentState();
            return;
        }

        if (activeState is RemovingState)
        {
            StopCurrentState();
            return;
        }

        if (activeCircuit == null || objectPlacer == null || inputManager == null)
            return;

        ChangeState(new RemovingState(objectPlacer, inputManager, wireSystem));
    }

    private void HandlePlacementCompleted()
    {
        if (activeState is PlacementState)
            StopCurrentState();
    }

    private void HandleWiringCompleted()
    {
        if (activeState is WiringState)
            StopCurrentState();
    }

    private void ChangeState(IPlacementState nextState)
    {
        StopCurrentState();
        if (nextState == null)
            return;

        activeState = nextState;
        if (activeState.Enter())
            return;

        IPlacementState failedState = activeState;
        activeState = null;
        failedState.Exit();
    }

    private void StopCurrentState()
    {
        if (activeState == null)
            return;

        IPlacementState stateToStop = activeState;
        activeState = null;
        stateToStop.Exit();
    }

    private bool TryConfigureCircuitBoard(out CircuitBoardContext board)
    {
        board = null;
        if (activeCircuit == null)
            return false;

        Transform tileGrid = activeCircuit.transform.Find(TileGridName);
        if (tileGrid == null)
        {
            Debug.LogWarning("Inspected circuit has no direct TileGrid child. Cannot edit circuit components.", activeCircuit);
            return false;
        }

        MeshFilter tileGridMesh = tileGrid.GetComponent<MeshFilter>();
        if (tileGridMesh == null || tileGridMesh.sharedMesh == null)
        {
            Debug.LogWarning("TileGrid has no mesh. Cannot edit circuit components.", tileGrid);
            return false;
        }

        Bounds tileGridBounds = tileGridMesh.sharedMesh.bounds;
        if (tileGridBounds.size.x <= 0.0001f || tileGridBounds.size.z <= 0.0001f)
        {
            Debug.LogWarning("TileGrid mesh has invalid dimensions.", tileGrid);
            return false;
        }

        Vector3 gridRight = tileGrid.right.normalized;
        Vector3 gridForward = tileGrid.forward.normalized;
        Vector3 gridNormal = tileGrid.up.normalized;

        Vector3 preferredNormal = activeCircuit.GetPreferredViewDirection();
        if (preferredNormal.sqrMagnitude > 0.01f && Vector3.Dot(gridNormal, preferredNormal.normalized) < 0f)
            gridNormal = -gridNormal;

        float localCellWidth = tileGridBounds.size.x / CircuitGridSize;
        float localCellHeight = tileGridBounds.size.z / CircuitGridSize;
        float cellWorldWidth = tileGrid.TransformVector(Vector3.right * localCellWidth).magnitude;
        float cellWorldHeight = tileGrid.TransformVector(Vector3.forward * localCellHeight).magnitude;
        Plane placementPlane = new(gridNormal, tileGrid.TransformPoint(tileGridBounds.center));

        UpdateBoardShader(tileGrid);
        board = new CircuitBoardContext(
            activeCircuit,
            tileGrid,
            tileGridBounds,
            placementPlane,
            gridRight,
            gridForward,
            gridNormal,
            cellWorldWidth,
            cellWorldHeight,
            CircuitGridSize,
            CircuitGridSize,
            GridSurfaceOffset);
        return true;
    }

    private void UpdateBoardShader(Transform tileGrid)
    {
        Renderer tileGridRenderer = tileGrid.GetComponent<Renderer>();
        if (tileGridRenderer == null)
            return;

        tileGridPropertyBlock ??= new MaterialPropertyBlock();
        tileGridRenderer.GetPropertyBlock(tileGridPropertyBlock);
        tileGridPropertyBlock.SetVector(
            GridSizeProperty,
            new Vector4(CircuitGridSize, CircuitGridSize, 0f, 0f));
        tileGridPropertyBlock.SetVector(
            GridOffsetProperty,
            new Vector4(-0.5f, 0.5f, 0f, 0f));
        tileGridRenderer.SetPropertyBlock(tileGridPropertyBlock);
    }

    private void ResolveReferences()
    {
        if (inputManager == null)
            inputManager = FindFirstObjectByType<placementInputManager>();
        if (objectPlacer == null)
            objectPlacer = FindFirstObjectByType<ObjectPlacer>();
        if (wireSystem == null)
            wireSystem = FindFirstObjectByType<CircuitWireSystem>();
        if (wireSystem == null)
            wireSystem = gameObject.AddComponent<CircuitWireSystem>();
        if (simulationSystem == null)
            simulationSystem = GetComponent<CircuitSimulationSystem>();
        if (simulationSystem == null)
            simulationSystem = gameObject.AddComponent<CircuitSimulationSystem>();
        if (hoverDisplay == null)
            hoverDisplay = GetComponent<CircuitComponentHoverDisplay>();
        if (hoverDisplay == null)
            hoverDisplay = gameObject.AddComponent<CircuitComponentHoverDisplay>();
        if (objectPlacer != null)
            objectPlacer.SetWireSystem(wireSystem);
        simulationSystem.Configure(objectPlacer, wireSystem);
        hoverDisplay.Configure(inputManager, simulationSystem);
    }

    private void SubscribeToInput()
    {
        if (inputManager == null)
            return;

        inputManager.OnClicked -= HandlePrimaryAction;
        inputManager.OnRightClicked -= ToggleRemovalState;
        inputManager.OnExit -= StopCurrentState;
        inputManager.OnClicked += HandlePrimaryAction;
        inputManager.OnRightClicked += ToggleRemovalState;
        inputManager.OnExit += StopCurrentState;
    }

    private void UnsubscribeFromInput()
    {
        if (inputManager == null)
            return;

        inputManager.OnClicked -= HandlePrimaryAction;
        inputManager.OnRightClicked -= ToggleRemovalState;
        inputManager.OnExit -= StopCurrentState;
    }
}
