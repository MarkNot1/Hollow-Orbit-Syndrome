using System;
using UnityEngine;

public sealed class OpenCircuitSlot : MonoBehaviour
{
    [SerializeField]
    private string slotId;

    [SerializeField]
    private Vector3Int gridPosition;

    [SerializeField]
    private Vector2Int footprintSize;

    [SerializeField]
    private int rotationQuarterTurns;

    [SerializeField]
    private CircuitNode terminal1;

    [SerializeField]
    private CircuitNode terminal2;

    public string SlotId => slotId;
    public Vector3Int GridPosition => gridPosition;
    public Vector2Int FootprintSize => footprintSize;
    public int RotationQuarterTurns => rotationQuarterTurns;
    public CircuitNode Terminal1 => terminal1;
    public CircuitNode Terminal2 => terminal2;
    public void Initialize(
        string id,
        Vector3Int position,
        Vector2Int size,
        int quarterTurns,
        CircuitNode first,
        CircuitNode second)
    {
        slotId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        gridPosition = position;
        footprintSize = size;
        rotationQuarterTurns = ((quarterTurns % 4) + 4) % 4;
        terminal1 = first;
        terminal2 = second;
        terminal1?.Initialize(this);
        terminal2?.Initialize(this);
        RefreshLabels();
    }

    public int GetTerminalIndex(CircuitNode node)
        => node == terminal1 ? 0 : node == terminal2 ? 1 : -1;

    public Vector3Int GetTerminalGridCell(CircuitNode node)
    {
        CircuitBoardContext board = GetBoardContext();
        if (board != null && node != null &&
            board.TryWorldToCell(node.ConnectionPoint.position, out Vector3Int cell))
            return cell;
        return gridPosition;
    }

    public bool Overlaps(Vector3Int position, Vector2Int size)
        => position.x < gridPosition.x + footprintSize.x &&
            position.x + size.x > gridPosition.x &&
            position.y < gridPosition.y + footprintSize.y &&
            position.y + size.y > gridPosition.y;

    public bool IsExactReplacement(Vector3Int position, Vector2Int size)
        => position == gridPosition && size == footprintSize;

    public void RefreshLabels()
    {
        SetLabel(terminal1, "A");
        SetLabel(terminal2, "B");
    }

    private void SetLabel(CircuitNode node, string side)
    {
        if (node == null)
            return;

        string remote = "FLOAT";
        foreach (WireConnection wire in node.Connections)
        {
            CircuitNode other = wire?.GetOther(node);
            if (other == null)
                continue;

            if (other.Owner?.Element != null)
            {
                string entity = string.IsNullOrWhiteSpace(other.Owner.Element.EntityName)
                    ? other.Owner.Element.Kind.ToString()
                    : other.Owner.Element.EntityName;
                remote = $"{entity}_{other.TerminalId}";
                break;
            }

            if (other.SlotOwner != null)
            {
                remote = $"Open_{other.SlotOwner.SlotId}_{other.SlotOwner.GetTerminalIndex(other)}";
                break;
            }
        }

        string suffix = slotId.Length > 8 ? slotId.Substring(0, 8) : slotId;
        string label = $"OPEN_{remote}_{suffix}_{side}";
        node.SetTerminalId(label);
        node.SetTerminalLabel(label);
        node.SetNetName(label);
        node.name = label;
    }

    private CircuitBoardContext GetBoardContext()
    {
        placementSystem placement = FindFirstObjectByType<placementSystem>();
        return placement != null && placement.ActiveBoard?.Circuit != null &&
            transform.IsChildOf(placement.ActiveBoard.Circuit.transform)
                ? placement.ActiveBoard
                : null;
    }
}
