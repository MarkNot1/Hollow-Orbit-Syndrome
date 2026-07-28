using System;
using UnityEngine;

public class placementSystem : MonoBehaviour
{
    private const int CircuitGridSize = 10;
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

    private SpecialPrefabInteractable activeCircuit;
    private IPlacementState activeState;
    private MaterialPropertyBlock tileGridPropertyBlock;

    public CircuitBoardContext ActiveBoard { get; private set; }

    public event Action<CircuitBoardContext> ActiveBoardChanged;
    public event Action CircuitChanged;

    public void NotifyCircuitChanged()
        => CircuitChanged?.Invoke();

    private void OnEnable()
    {
        ValidateReferences();
        if (objectPlacer != null)
            objectPlacer.SetWireSystem(wireSystem);
        SubscribeToInput();
        SubscribeToCircuitChanges();
    }

    private void OnDisable()
    {
        UnsubscribeFromInput();
        UnsubscribeFromCircuitChanges();
        StopCurrentState();
        DeactivateBoard();
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
            DeactivateBoard();
            return;
        }

        ActiveBoard = board;
        objectPlacer.SetActiveBoard(board);
        if (wireSystem != null)
            wireSystem.SetActiveBoard(board, objectPlacer.ActiveGrid);
        ActiveBoardChanged?.Invoke(board);
    }

    public void ClearActiveCircuit()
    {
        StopCurrentState();
        activeCircuit = null;
        DeactivateBoard();
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

    private void HandleRotateAction()
    {
        if (activeState is PlacementState placementState)
            placementState.HandleRotateAction();
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

    private void ValidateReferences()
    {
        if (inputManager == null)
            Debug.LogError("Placement input manager is not assigned.", this);
        if (objectPlacer == null)
            Debug.LogError("ObjectPlacer is not assigned.", this);
        if (wireSystem == null)
            Debug.LogError("CircuitWireSystem is not assigned.", this);
    }

    private void DeactivateBoard()
    {
        if (objectPlacer != null)
            objectPlacer.ClearActiveBoard();
        if (wireSystem != null)
            wireSystem.ClearActiveBoard();

        bool hadBoard = ActiveBoard != null;
        ActiveBoard = null;
        if (hadBoard)
            ActiveBoardChanged?.Invoke(null);
    }

    private void SubscribeToCircuitChanges()
    {
        CircuitElement.ElectricalValueChanged += HandleElectricalValueChanged;
        if (objectPlacer != null)
        {
            objectPlacer.ComponentPlaced += HandleComponentChanged;
            objectPlacer.ComponentRemoved += HandleComponentChanged;
        }
        if (wireSystem != null)
        {
            wireSystem.ConnectionCreated += HandleConnectionChanged;
            wireSystem.ConnectionRemoved += HandleConnectionChanged;
        }
    }

    private void UnsubscribeFromCircuitChanges()
    {
        CircuitElement.ElectricalValueChanged -= HandleElectricalValueChanged;
        if (objectPlacer != null)
        {
            objectPlacer.ComponentPlaced -= HandleComponentChanged;
            objectPlacer.ComponentRemoved -= HandleComponentChanged;
        }
        if (wireSystem != null)
        {
            wireSystem.ConnectionCreated -= HandleConnectionChanged;
            wireSystem.ConnectionRemoved -= HandleConnectionChanged;
        }
    }

    private void HandleComponentChanged(PlacedCircuitComponent _)
        => CircuitChanged?.Invoke();

    private void HandleConnectionChanged(WireConnection _)
        => CircuitChanged?.Invoke();

    private void HandleElectricalValueChanged(CircuitElement element)
    {
        if (element != null && ActiveBoard?.Circuit != null &&
            element.transform.IsChildOf(ActiveBoard.Circuit.transform))
            CircuitChanged?.Invoke();
    }

    private void SubscribeToInput()
    {
        if (inputManager == null)
            return;

        inputManager.OnClicked -= HandlePrimaryAction;
        inputManager.OnRightClicked -= ToggleRemovalState;
        inputManager.OnExit -= StopCurrentState;
        inputManager.OnRotate -= HandleRotateAction;
        inputManager.OnClicked += HandlePrimaryAction;
        inputManager.OnRightClicked += ToggleRemovalState;
        inputManager.OnExit += StopCurrentState;
        inputManager.OnRotate += HandleRotateAction;
    }

    private void UnsubscribeFromInput()
    {
        if (inputManager == null)
            return;

        inputManager.OnClicked -= HandlePrimaryAction;
        inputManager.OnRightClicked -= ToggleRemovalState;
        inputManager.OnExit -= StopCurrentState;
        inputManager.OnRotate -= HandleRotateAction;
    }
}
