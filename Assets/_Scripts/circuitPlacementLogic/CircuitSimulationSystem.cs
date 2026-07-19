using System;
using UnityEngine;

public sealed class CircuitSimulationSystem : MonoBehaviour
{
    private ObjectPlacer objectPlacer;
    private CircuitWireSystem wireSystem;
    private CircuitBoardContext activeBoard;
    private bool rebuildPending;

    public CircuitTopologyResult CurrentTopology { get; private set; }
    public CircuitSimulationResult CurrentResult { get; private set; }

    public event Action<CircuitSimulationResult> SimulationUpdated;

    public void Configure(ObjectPlacer placer, CircuitWireSystem wires)
    {
        Unsubscribe();
        objectPlacer = placer;
        wireSystem = wires;
        Subscribe();
    }

    public void SetActiveBoard(CircuitBoardContext board)
    {
        activeBoard = board;
        ScheduleRebuild();
    }

    public void ClearActiveBoard()
    {
        activeBoard = null;
        rebuildPending = false;
        CurrentTopology = null;
        CurrentResult = CircuitSimulationResult.Failed("No active circuit board.");
        SimulationUpdated?.Invoke(CurrentResult);
    }

    public void ScheduleRebuild()
        => rebuildPending = activeBoard?.Circuit != null;

    public CircuitSimulationResult RebuildNow()
    {
        rebuildPending = false;
        if (activeBoard?.Circuit == null)
        {
            CurrentTopology = null;
            CurrentResult = CircuitSimulationResult.Failed("No active circuit board.");
        }
        else
        {
            PlacedCircuitComponent[] components =
                activeBoard.Circuit.GetComponentsInChildren<PlacedCircuitComponent>(true);
            CurrentTopology = CircuitTopology.Build(components);
            CurrentResult = CircuitSimulationService.Simulate(CurrentTopology);
        }

        SimulationUpdated?.Invoke(CurrentResult);
        return CurrentResult;
    }

    private void LateUpdate()
    {
        if (rebuildPending)
            RebuildNow();
    }

    private void OnDestroy()
        => Unsubscribe();

    private void Subscribe()
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

    private void Unsubscribe()
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
        => ScheduleRebuild();

    private void HandleConnectionChanged(WireConnection _)
        => ScheduleRebuild();

    private void HandleElectricalValueChanged(CircuitElement element)
    {
        if (element != null && activeBoard?.Circuit != null &&
            element.transform.IsChildOf(activeBoard.Circuit.transform))
        {
            ScheduleRebuild();
        }
    }
}
