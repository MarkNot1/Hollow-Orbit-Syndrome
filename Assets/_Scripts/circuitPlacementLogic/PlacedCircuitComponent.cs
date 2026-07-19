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
    public CircuitElement Element { get; private set; }

    public void Initialize(int id, Vector3Int position, Vector2Int size)
    {
        componentId = id;
        gridPosition = position;
        footprintSize = size;
        ConfigureNodes();
    }

    public void ConfigureNodes()
    {
        CircuitNode[] nodes = GetComponentsInChildren<CircuitNode>(true);
        if (nodes.Length == 0)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child != transform && child.name.StartsWith("Node_", System.StringComparison.OrdinalIgnoreCase))
                    child.gameObject.AddComponent<CircuitNode>();
            }

            nodes = GetComponentsInChildren<CircuitNode>(true);
        }

        foreach (CircuitNode node in nodes)
            node.Initialize(this);

        Element = GetComponent<CircuitElement>();
        if (Element == null && componentId >= 0 && componentId <= 4)
            Element = gameObject.AddComponent<CircuitElement>();

        if (Element != null)
        {
            Element.ConfigureFromComponentId(componentId);
            Element.Initialize(this);
        }
    }
}
