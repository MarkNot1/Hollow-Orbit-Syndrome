using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CircuitSimulationSystem : MonoBehaviour
{
    [SerializeField]
    private placementSystem placementSystem;

    private CircuitBoardContext activeBoard;
    private bool rebuildPending;
    private CircuitTopologyResult currentTopology;
    private bool suppressNextBatteryShortBurst;
    private readonly Dictionary<CircuitElement, float> unsafeDurations = new();

    public CircuitSimulationResult CurrentResult { get; private set; }

    public event Action<CircuitSimulationResult> SimulationUpdated;

    public void SuppressNextBatteryShortBurst()
        => suppressNextBatteryShortBurst = true;

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
        CircuitElement.StateChanged += HandleElementStateChanged;
        HandleActiveBoardChanged(placementSystem.ActiveBoard);
    }

    private void OnDisable()
    {
        if (placementSystem != null)
        {
            placementSystem.ActiveBoardChanged -= HandleActiveBoardChanged;
            placementSystem.CircuitChanged -= ScheduleRebuild;
        }
        CircuitElement.StateChanged -= HandleElementStateChanged;

        SetBoardLedCurrent(activeBoard, 0d);
        activeBoard = null;
        rebuildPending = false;
    }

    private void HandleActiveBoardChanged(CircuitBoardContext board)
    {
        SetBoardLedCurrent(activeBoard, 0d);
        activeBoard = board;
        SetBoardLedCurrent(activeBoard, 0d);
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
            CircuitNode[] openNodes = GetOpenNodes(activeBoard);
            currentTopology = CircuitTopology.Build(components, openNodes);
            UpdateBatteryShortState(currentTopology, !suppressNextBatteryShortBurst);
            suppressNextBatteryShortBurst = false;
            CurrentResult = currentTopology.HasBatteryShort
                ? CircuitSimulationResult.Failed("Battery terminals are shorted. Remove the faulty connection.")
                : CircuitSimulationService.Simulate(currentTopology);
            ApplyLedCurrents(currentTopology, CurrentResult);
        }

        SimulationUpdated?.Invoke(CurrentResult);
        return CurrentResult;
    }

    private void LateUpdate()
    {
        if (rebuildPending)
            RebuildNow();
    }

    private void Update()
    {
        if (currentTopology == null || CurrentResult == null || !CurrentResult.Success)
        {
            unsafeDurations.Clear();
            return;
        }

        foreach (CircuitElement element in currentTopology.Elements)
        {
            if (element == null || element.Kind == CircuitElementKind.Battery || element.IsBroken ||
                !CurrentResult.Elements.TryGetValue(element, out CircuitElementResult result) ||
                !element.IsUnsafe(result, out string reason))
            {
                unsafeDurations.Remove(element);
                continue;
            }

            float duration = unsafeDurations.TryGetValue(element, out float existing)
                ? existing + Time.deltaTime
                : Time.deltaTime;
            unsafeDurations[element] = duration;
            if (duration >= element.FailureDelay && element.Break(reason))
            {
                unsafeDurations.Remove(element);
                rebuildPending = true;
            }
        }
    }

    private void ClearResult()
    {
        SetBoardLedCurrent(activeBoard, 0d);
        activeBoard = null;
        rebuildPending = false;
        CurrentResult = CircuitSimulationResult.Failed("No active circuit board.");
        currentTopology = null;
        unsafeDurations.Clear();
        SimulationUpdated?.Invoke(CurrentResult);
    }

    private static void ApplyLedCurrents(
        CircuitTopologyResult topology,
        CircuitSimulationResult simulation)
    {
        foreach (CircuitElement element in topology.Elements)
        {
            if (element.Kind != CircuitElementKind.Led)
                continue;

            double current = 0d;
            if (simulation.Success &&
                simulation.Elements.TryGetValue(element, out CircuitElementResult result))
            {
                current = result.Current;
            }

            element.SetLedCurrent(current);
        }
    }

    private static void SetBoardLedCurrent(CircuitBoardContext board, double current)
    {
        if (board?.Circuit == null)
            return;

        CircuitElement[] elements = board.Circuit.GetComponentsInChildren<CircuitElement>(true);
        foreach (CircuitElement element in elements)
        {
            if (element.Kind == CircuitElementKind.Led)
                element.SetLedCurrent(current);
        }
    }

    private static CircuitNode[] GetOpenNodes(CircuitBoardContext board)
        => board?.Circuit == null
            ? Array.Empty<CircuitNode>()
            : Array.FindAll(
                board.Circuit.GetComponentsInChildren<CircuitNode>(true),
                node => node != null && node.SlotOwner != null);

    private static void UpdateBatteryShortState(
        CircuitTopologyResult topology,
        bool allowBurst)
    {
        foreach (CircuitElement element in topology.Elements)
        {
            if (element.Kind != CircuitElementKind.Battery)
                continue;

            bool shorted = element == topology.ShortedBattery;
            element.SetBatteryShorted(
                shorted,
                allowBurst && shorted && !element.IsBatteryShorted);
        }
    }

    private void HandleElementStateChanged(CircuitElement element)
    {
        if (element != null && activeBoard?.Circuit != null &&
            element.transform.IsChildOf(activeBoard.Circuit.transform))
            rebuildPending = true;
    }
}
