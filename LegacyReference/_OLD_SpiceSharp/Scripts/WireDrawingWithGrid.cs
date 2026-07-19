using SpiceSharp;
using UnityEngine;

public class WireDrawingWithGrid : MonoBehaviour
{
    public static WireDrawingWithGrid Instance;
    public Canvas MainCanvas;
    public Circuit_ComponentNode nodePrefab;
    public RectTransform CircuitBoard;
    public Circuit_Wire wirePrefab;
    Circuit_Wire CurrentWire;
    public RectTransform WireParent;
    public bool isDrawing = false;
    private Vector3 startPosition;
  
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    void Update()
    {
        //if (!Circuit_ConnectionManager.Instance.AddingWires)
        //    return;
        if (Circuit_ConnectionManager.Instance.IsMouseOverComponent)
            return;
        if (!Circuit_Utilities.IsPointerOverCircuitBoard(CircuitBoard, MainCanvas))
            return;
        // Detect mouse click to start drawing the wire
        if (Input.GetMouseButtonDown(0))
        {
            StartDrawing();
        }

        // Continue drawing while the mouse is held down
        if (isDrawing)
        {
            UpdateLine();
        }

        // End the line when the mouse button is released
        if (Input.GetMouseButtonUp(0) && isDrawing)
        {
            EndDrawing();
        }
    }
   
    void StartDrawing()
    {
    
        if (Circuit_ConnectionManager.Instance.currentHoveredNode != null)
        {
            // Get the mouse position, snap it to the grid, and set the start position
            startPosition = Circuit_ConnectionManager.Instance.currentHoveredNode.transform.position;
            CurrentWire = Instantiate(wirePrefab, WireParent);
            CurrentWire.lineRenderer.positionCount = 2;
            CurrentWire.lineRenderer.SetPosition(0, startPosition);
            CurrentWire.Nodes.Add(Circuit_ConnectionManager.Instance.currentHoveredNode);
            Circuit_ConnectionManager.Instance.currentHoveredNode.connectedWithWires.Add(CurrentWire);
            CurrentWire.lineRenderer.SetPosition(0, Circuit_ConnectionManager.Instance.currentHoveredNode.transform.position);
            //Circuit_ConnectionManager.Instance.AddWire(CurrentWire);
            //LineSelector.Instance.AddLineRenderer(CurrentWire.lineRenderer);
            isDrawing = true;
        }
        else
        {
        }
    
    }

    void UpdateLine()
    {
        // Continuously update the wire as the mouse moves
        Vector3 currentMousePosition = Circuit_Utilities.SnapToGrid(Circuit_Utilities.GetMouseWorldPosition());

        // Update the positions of the LineRenderer
        CurrentWire.lineRenderer.SetPosition(1, currentMousePosition); 
    }

    void EndDrawing()
    {
        // Finalize the wire when the mouse is released
        Vector3 endPosition = Circuit_Utilities.SnapToGrid(Circuit_Utilities.GetMouseWorldPosition());
        isDrawing = false;
        if (Circuit_ConnectionManager.Instance.currentHoveredNode != null)
        {
            // Set the final positions in the LineRenderer
            CurrentWire.lineRenderer.SetPosition(1, endPosition);
            CurrentWire.Nodes.Add(Circuit_ConnectionManager.Instance.currentHoveredNode);
            Circuit_ConnectionManager.Instance.currentHoveredNode.connectedWithWires.Add(CurrentWire);
            CurrentWire.lineRenderer.SetPosition(1, Circuit_ConnectionManager.Instance.currentHoveredNode.transform.position);
            Circuit_ConnectionManager.Instance.TryCompletecircuit();
        }
        else
        {
            CurrentWire.Nodes[0].connectedWithWires.Remove(CurrentWire);
            Circuit_ConnectionManager.Instance.RemoveWire(CurrentWire);
            LineSelector.Instance.lineRenderers.Remove(CurrentWire.lineRenderer);
            Destroy(CurrentWire.gameObject);
            return;
        }
       
    }

    Vector3[] GenerateRightAnglePoints(Vector3 start, Vector3 end)
    {
        // Generates right-angle points for the wire
        Vector3[] points = new Vector3[2];

        // Determine whether to go horizontally first or vertically first
        bool goHorizontalFirst = Mathf.Abs(end.x - start.x) > Mathf.Abs(end.y - start.y);

        if (goHorizontalFirst)
        {
            // First move horizontally, then vertically
            points[0] = new Vector3(end.x, start.y, start.z); // Bend horizontally
            points[1] = end; // End point
        }
        else
        {
            // First move vertically, then horizontally
            points[0] = new Vector3(start.x, end.y, start.z); // Bend vertically
            points[1] = end; // End point
        }

        return points;
    }
}
