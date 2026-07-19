using System.Collections.Generic;
using UnityEngine;

public sealed class CircuitNode : MonoBehaviour
{
    [SerializeField]
    private string terminalId;

    [SerializeField]
    private Vector2Int cellOffset;

    [SerializeField]
    private Transform connectionPoint;

    [SerializeField]
    private string terminalLabel;

    [SerializeField]
    private string netName;

    private readonly HashSet<WireConnection> connections = new();

    public string TerminalId => string.IsNullOrWhiteSpace(terminalId) ? name : terminalId;
    public string TerminalLabel => terminalLabel;
    public string NetName => string.IsNullOrWhiteSpace(netName) ? terminalLabel : netName;
    public Vector2Int CellOffset => cellOffset;
    public Transform ConnectionPoint => connectionPoint != null ? connectionPoint : transform;
    public PlacedCircuitComponent Owner { get; private set; }
    public IReadOnlyCollection<WireConnection> Connections => connections;

    public void Initialize(PlacedCircuitComponent owner)
    {
        Owner = owner;
        if (string.IsNullOrWhiteSpace(terminalId))
            terminalId = name;

        int nodeLayer = LayerMask.NameToLayer("CircuitNode");
        if (nodeLayer >= 0)
            gameObject.layer = nodeLayer;
    }

    public Vector3Int GetGridCell()
    {
        if (Owner == null)
            return default;

        return Owner.GridPosition + new Vector3Int(cellOffset.x, cellOffset.y, 0);
    }

    public bool IsConnectedTo(CircuitNode other)
    {
        foreach (WireConnection connection in connections)
        {
            if (connection != null && connection.Connects(this, other))
                return true;
        }

        return false;
    }

    internal void Attach(WireConnection connection)
    {
        if (connection != null)
            connections.Add(connection);
    }

    internal void Detach(WireConnection connection)
        => connections.Remove(connection);

    internal void SetTerminalLabel(string value)
    {
        terminalLabel = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(netName))
            netName = terminalLabel;
    }

    internal void SetNetName(string value)
        => netName = value?.Trim() ?? string.Empty;

    public static bool WouldShortSupply(CircuitNode first, CircuitNode second)
    {
        if (first == null || second == null)
            return false;

        return IsSupply(first.NetName) && IsGround(second.NetName) ||
            IsGround(first.NetName) && IsSupply(second.NetName);
    }

    private static bool IsSupply(string value)
        => string.Equals(value, "VCC", System.StringComparison.OrdinalIgnoreCase);

    private static bool IsGround(string value)
        => string.Equals(value, "GND", System.StringComparison.OrdinalIgnoreCase) || value == "0";
}
