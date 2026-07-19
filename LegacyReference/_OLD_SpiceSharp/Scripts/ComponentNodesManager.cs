using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ComponentNodesManager : MonoBehaviour
{
    public static ComponentNodesManager Instance;
    public Circuit_ComponentNode nodePrefab;
    public RectTransform CircuitBoard;
    public Canvas MainCanvas;
    public List<Circuit_ComponentNode> nodes;
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!Circuit_ConnectionManager.Instance.AddingNodes)
            return;
        if (Circuit_ConnectionManager.Instance.IsMouseOverComponent)
            return;
        if (!Circuit_Utilities.IsPointerOverCircuitBoard(CircuitBoard, MainCanvas))
            return;
        if (Input.GetMouseButtonDown(0))
        {
            Circuit_ComponentNode newNode = Instantiate(nodePrefab, CircuitBoard);
            newNode.transform.position = Circuit_Utilities.SnapToGrid(Circuit_Utilities.GetMouseWorldPosition());
            newNode.gameObject.name = "Node" + nodes.Count;
      //      nodes.Add(newNode);
        }
    }
    public void RemoveAllNodes()
    {

        foreach (var node in nodes)
        {
            Destroy(node.gameObject);
        }
        nodes.Clear();
    }
}
