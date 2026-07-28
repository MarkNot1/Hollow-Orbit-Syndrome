using System;
using UnityEngine;

public enum CircuitElementKind
{
    Resistor,
    Capacitor,
    Led,
    Diode,
    Battery
}

public enum CircuitComponentState
{
    Healthy,
    Broken,
    BatteryShorted
}

public sealed class CircuitElement : MonoBehaviour
{
    public static event Action<CircuitElement> ElectricalValueChanged;
    public static event Action<CircuitElement> StateChanged;
    public const double BrokenResistance = 1e12d;

    [SerializeField]
    private CircuitElementKind kind;

    [SerializeField, Min(0.000001f)]
    private double value = 1000d;

    [SerializeField, Min(1e-20f)]
    private double saturationCurrent = 1e-14d;

    [SerializeField, Min(0.1f)]
    private double emissionCoefficient = 1d;

    [SerializeField]
    private CircuitNode terminal1;

    [SerializeField]
    private CircuitNode terminal2;

    [SerializeField, HideInInspector]
    private bool defaultsInitialized;

    [SerializeField, Min(0f)]
    private double ledTurnOnCurrent = 0.0001d;

    [Header("Failure ratings (zero disables a rating)")]
    [SerializeField, Min(0f)]
    private double maximumVoltage;

    [SerializeField, Min(0f)]
    private double maximumCurrent;

    [SerializeField, Min(0f)]
    private double maximumPower;

    [SerializeField, Min(0f)]
    private double maximumReverseVoltage;

    [SerializeField, Min(0.01f)]
    private float failureDelay = 0.35f;

    [SerializeField, HideInInspector]
    private bool failureDefaultsInitialized;

    [SerializeField, HideInInspector]
    private CircuitComponentState state;

    [SerializeField, HideInInspector]
    private string failureReason;

    private Light[] ledLights;
    private BrokenComponentVisual brokenVisual;

    private const double DefaultLedTurnOnCurrent = 0.0001d;

    public CircuitElementKind Kind => kind;
    public double Value => value;
    public double SaturationCurrent => saturationCurrent;
    public double EmissionCoefficient => emissionCoefficient;
    public CircuitNode Terminal1 => terminal1;
    public CircuitNode Terminal2 => terminal2;
    public PlacedCircuitComponent Owner { get; private set; }
    public string Prefix => GetPrefix(kind);
    public string EntityName { get; internal set; }
    public CircuitComponentState State => state;
    public bool IsBroken => state == CircuitComponentState.Broken;
    public bool IsBatteryShorted => state == CircuitComponentState.BatteryShorted;
    public string FailureReason => failureReason;
    public double MaximumVoltage => maximumVoltage;
    public double MaximumCurrent => maximumCurrent;
    public double MaximumPower => maximumPower;
    public double MaximumReverseVoltage => maximumReverseVoltage;
    public float FailureDelay => Mathf.Max(0.01f, failureDelay);
    public double EffectiveResistance => IsBroken ? BrokenResistance : value;

    private void Awake()
    {
        brokenVisual = GetComponent<BrokenComponentVisual>();
        brokenVisual?.ApplyLoadedState(IsBroken);
        if (Application.isPlaying && kind == CircuitElementKind.Led)
            SetLedCurrent(0d);
    }

    public void Initialize(PlacedCircuitComponent owner)
    {
        Owner = owner;
        ResolveTerminals();
        if (kind == CircuitElementKind.Led)
            SetLedCurrent(0d);
    }

    public void ConfigureFromComponentId(int componentId)
    {
        kind = componentId switch
        {
            0 => CircuitElementKind.Resistor,
            1 => CircuitElementKind.Capacitor,
            2 => CircuitElementKind.Led,
            3 => CircuitElementKind.Diode,
            4 => CircuitElementKind.Battery,
            _ => kind
        };

        if (!defaultsInitialized)
        {
            value = kind switch
            {
                CircuitElementKind.Battery => 5d,
                CircuitElementKind.Capacitor => 1e-6d,
                _ => value > 0d ? value : 1000d
            };

            if (kind == CircuitElementKind.Led)
            {
                saturationCurrent = 1e-20d;
                emissionCoefficient = 2d;
            }

            defaultsInitialized = true;
        }

        if (!failureDefaultsInitialized)
        {
            ConfigureFailureDefaults(kind);
            failureDefaultsInitialized = true;
        }
    }

    public bool HasValidTerminals
        => terminal1 != null && terminal2 != null && terminal1 != terminal2;

    internal void AssignTerminalLabels(string first, string second)
    {
        ResolveTerminals();
        terminal1?.SetTerminalLabel(first);
        terminal2?.SetTerminalLabel(second);
    }

    public bool TrySetValue(double newValue)
    {
        if (!double.IsFinite(newValue) || newValue <= 0d || value == newValue)
            return false;

        value = newValue;
        ElectricalValueChanged?.Invoke(this);
        return true;
    }

    public bool IsUnsafe(CircuitElementResult result, out string reason)
    {
        reason = string.Empty;
        if (kind == CircuitElementKind.Battery || IsBroken || result == null)
            return false;

        double voltage = result.Voltage;
        double current = Math.Abs(result.Current);
        double power = Math.Abs(result.Power);
        if (maximumReverseVoltage > 0d && voltage < -maximumReverseVoltage)
            reason = $"Reverse voltage exceeded {maximumReverseVoltage:g4} V.";
        else if (maximumVoltage > 0d && Math.Abs(voltage) > maximumVoltage)
            reason = $"Voltage exceeded {maximumVoltage:g4} V.";
        else if (maximumCurrent > 0d && current > maximumCurrent)
            reason = $"Current exceeded {maximumCurrent:g4} A.";
        else if (maximumPower > 0d && power > maximumPower)
            reason = $"Power exceeded {maximumPower:g4} W.";

        return reason.Length > 0;
    }

    public bool Break(string reason, bool playBurst = true)
    {
        if (kind == CircuitElementKind.Battery || IsBroken)
            return false;

        state = CircuitComponentState.Broken;
        failureReason = string.IsNullOrWhiteSpace(reason) ? "Electrical overload." : reason.Trim();
        SetLedCurrent(0d);
        brokenVisual ??= GetComponent<BrokenComponentVisual>();
        if (playBurst)
            brokenVisual?.PlayFailure();
        else
            brokenVisual?.ApplyLoadedState(true);
        StateChanged?.Invoke(this);
        return true;
    }

    public void RestoreState(CircuitComponentState restoredState, string reason)
    {
        state = kind == CircuitElementKind.Battery
            ? CircuitComponentState.Healthy
            : restoredState == CircuitComponentState.Broken
                ? CircuitComponentState.Broken
                : CircuitComponentState.Healthy;
        failureReason = state == CircuitComponentState.Broken ? reason?.Trim() ?? string.Empty : string.Empty;
        brokenVisual ??= GetComponent<BrokenComponentVisual>();
        brokenVisual?.ApplyLoadedState(IsBroken);
        StateChanged?.Invoke(this);
    }

    public bool SetBatteryShorted(bool shorted, bool playBurst)
    {
        if (kind != CircuitElementKind.Battery)
            return false;

        CircuitComponentState next = shorted
            ? CircuitComponentState.BatteryShorted
            : CircuitComponentState.Healthy;
        if (state == next)
            return false;

        state = next;
        failureReason = shorted ? "Battery terminals are shorted." : string.Empty;
        brokenVisual ??= GetComponent<BrokenComponentVisual>();
        if (shorted && playBurst)
            brokenVisual?.PlayBatteryShortBurst();
        StateChanged?.Invoke(this);
        return true;
    }

    public void SetLedCurrent(double current)
    {
        if (kind != CircuitElementKind.Led)
            return;

        double threshold = ledTurnOnCurrent > 0d
            ? ledTurnOnCurrent
            : DefaultLedTurnOnCurrent;
        bool shouldIlluminate = double.IsFinite(current) && current >= threshold;

        if (ledLights == null)
            ledLights = GetComponentsInChildren<Light>(true);

        foreach (Light ledLight in ledLights)
        {
            if (ledLight != null)
                ledLight.enabled = shouldIlluminate;
        }
    }

    private void ResolveTerminals()
    {
        if (terminal1 != null && terminal2 != null && terminal1 != terminal2)
            return;

        CircuitNode[] nodes = GetComponentsInChildren<CircuitNode>(true);
        Array.Sort(nodes, CompareNodes);
        terminal1 = nodes.Length > 0 ? nodes[0] : null;
        terminal2 = nodes.Length > 1 ? nodes[1] : null;
    }

    private static int CompareNodes(CircuitNode first, CircuitNode second)
    {
        int firstIndex = ParseTerminalIndex(first != null ? first.TerminalId : string.Empty);
        int secondIndex = ParseTerminalIndex(second != null ? second.TerminalId : string.Empty);
        int indexComparison = firstIndex.CompareTo(secondIndex);
        return indexComparison != 0
            ? indexComparison
            : string.Compare(first?.TerminalId, second?.TerminalId, StringComparison.Ordinal);
    }

    private static int ParseTerminalIndex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return int.MaxValue;

        int start = value.Length;
        while (start > 0 && char.IsDigit(value[start - 1]))
            start--;

        return start < value.Length && int.TryParse(value.Substring(start), out int index)
            ? index
            : int.MaxValue;
    }

    public static string GetPrefix(CircuitElementKind elementKind)
        => elementKind switch
        {
            CircuitElementKind.Resistor => "R",
            CircuitElementKind.Capacitor => "C",
            CircuitElementKind.Led => "L",
            CircuitElementKind.Diode => "D",
            CircuitElementKind.Battery => "B",
            _ => "N"
        };

    private void ConfigureFailureDefaults(CircuitElementKind elementKind)
    {
        failureDelay = 0.35f;
        switch (elementKind)
        {
            case CircuitElementKind.Resistor:
                maximumVoltage = 25d;
                maximumCurrent = 0.05d;
                maximumPower = 0.25d;
                maximumReverseVoltage = 0d;
                break;
            case CircuitElementKind.Capacitor:
                maximumVoltage = 10d;
                maximumCurrent = 0d;
                maximumPower = 0d;
                maximumReverseVoltage = 0.5d;
                break;
            case CircuitElementKind.Led:
                maximumVoltage = 3.5d;
                maximumCurrent = 0.025d;
                maximumPower = 0.1d;
                maximumReverseVoltage = 5d;
                break;
            case CircuitElementKind.Diode:
                maximumVoltage = 0d;
                maximumCurrent = 1d;
                maximumPower = 1d;
                maximumReverseVoltage = 50d;
                break;
            case CircuitElementKind.Battery:
                maximumVoltage = 0d;
                maximumCurrent = 0d;
                maximumPower = 0d;
                maximumReverseVoltage = 0d;
                state = CircuitComponentState.Healthy;
                break;
        }
    }
}
