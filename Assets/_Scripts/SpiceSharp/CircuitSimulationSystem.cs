using System;
using UnityEngine;

public sealed class CircuitSimulationSystem : MonoBehaviour
{
    [SerializeField]
    private placementSystem placementSystem;

    private CircuitBoardContext activeBoard;
    private bool rebuildPending;

    public CircuitSimulationResult CurrentResult { get; private set; }

    public event Action<CircuitSimulationResult> SimulationUpdated;

    private void OnEnable()
    {
        if (placementSystem == null)
        {
            Debug.LogError("Placement system is not assigned to CircuitSimulationSystem.", this);
            ClearResult();
            return;
        }

        placementSystem.ActiveBoardChanged += HandleActiveBoardChanged;
        placementSystem.CircuitChanged += ScheduleRebuild;
        HandleActiveBoardChanged(placementSystem.ActiveBoard);
    }

    private void OnDisable()
    {
        if (placementSystem != null)
        {
            placementSystem.ActiveBoardChanged -= HandleActiveBoardChanged;
            placementSystem.CircuitChanged -= ScheduleRebuild;
        }

        activeBoard = null;
        rebuildPending = false;
    }

    private void HandleActiveBoardChanged(CircuitBoardContext board)
    {
        activeBoard = board;
        if (activeBoard?.Circuit != null)
            rebuildPending = true;
        else
            ClearResult();
    }

    private void ScheduleRebuild()
        => rebuildPending = activeBoard?.Circuit != null;

    public CircuitSimulationResult RebuildNow()
    {
        rebuildPending = false;
        if (activeBoard?.Circuit == null)
        {
            CurrentResult = CircuitSimulationResult.Failed("No active circuit board.");
        }
        else
        {
            PlacedCircuitComponent[] components =
                activeBoard.Circuit.GetComponentsInChildren<PlacedCircuitComponent>(true);
            CircuitTopologyResult topology = CircuitTopology.Build(components);
            CurrentResult = CircuitSimulationService.Simulate(topology);
        }

        SimulationUpdated?.Invoke(CurrentResult);
        return CurrentResult;
    }

    private void LateUpdate()
    {
        if (rebuildPending)
            RebuildNow();
    }

    private void ClearResult()
    {
        activeBoard = null;
        rebuildPending = false;
        CurrentResult = CircuitSimulationResult.Failed("No active circuit board.");
        SimulationUpdated?.Invoke(CurrentResult);
    }
}
