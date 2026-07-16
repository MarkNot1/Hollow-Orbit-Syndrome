using UnityEngine;

public class PlacedCircuitComponent : MonoBehaviour
{
    [SerializeField]
    private int componentId = -1;

    [SerializeField]
    private Vector3Int gridPosition;

    [SerializeField]
    private Vector2Int footprintSize = Vector2Int.one;

    public int ComponentId => componentId;
    public Vector3Int GridPosition => gridPosition;
    public Vector2Int FootprintSize => footprintSize;

    public void Initialize(int id, Vector3Int position, Vector2Int size)
    {
        componentId = id;
        gridPosition = position;
        footprintSize = size;
    }
}
