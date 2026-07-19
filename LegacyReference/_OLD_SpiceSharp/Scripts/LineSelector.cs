using System.Collections.Generic;
using UnityEngine;

public class LineSelector : MonoBehaviour
{
    public static LineSelector Instance { get; private set; }
    public Color highlightColor = Color.yellow;  // Highlight color for the line
    public Color selectedColor = Color.red;      // Selected line color
    public float selectionThreshold = 10f;       // Threshold for selection sensitivity

    private LineRenderer selectedLine = null;    // Currently selected line
    private Camera uiCamera;                     // Reference to the camera used for UI rendering

    public List<LineRenderer> lineRenderers ;

    bool SetToRemoveLine;
    bool SetToDisplayStats;
    private void Awake()
    {
        if(Instance == null)
            Instance = this;
    }
    void Start()
    {

        uiCamera = Camera.main;  // Reference to the main camera (which should be rendering the UI canvas in screen space)
    }

    void Update()
    {
        DetectLineHover();
        SelectLineOnClick();
    }
    public void AddLineRenderer(LineRenderer lineRenderer)
    {
        lineRenderers.Add(lineRenderer);
    }
    // Detect if the mouse is hovering over a line
    void DetectLineHover()
    {
        if (selectedLine == null) // Only highlight if no line is selected
        {
            Vector2 mousePos = Input.mousePosition;

            // Loop through all LineRenderers in the scene
            foreach (LineRenderer line in lineRenderers)
            {
                if(line == null) continue;
                if (IsMouseNearLine(line, mousePos))
                {
                    HighlightLine(line);
                }
                else
                {
                    ResetLineColor(line);
                }
            }
        }
    }
    public void RemoveAllLines()
    {
        foreach (LineRenderer lineSelector in lineRenderers)
        {
            Destroy(lineSelector.gameObject);
        }
        lineRenderers.Clear();
    }
    // Select the line when the mouse clicks on it
    void SelectLineOnClick()
    {
        if (Input.GetMouseButtonDown(1)) // Left mouse click
        {
            Vector2 mousePos = Input.mousePosition;

            foreach (LineRenderer line in lineRenderers)
            {
                if (line == null)
                    continue;
                if (IsMouseNearLine(line, mousePos))
                {
                    // Reset previously selected line
                    if (selectedLine != null)
                    {
                        ResetLineColor(selectedLine);
                    }


                    selectedLine = line;
                    selectedLine.GetComponentInParent<Circuit_Wire>().DetatchNodes();
                    lineRenderers.Remove(selectedLine);
                    ResetLineColor(selectedLine);
                    selectedLine = null;
                    break;
                }
            }
        }
    }

    // Checks if the mouse is near a line segment (in 2D space)
    bool IsMouseNearLine(LineRenderer line, Vector2 mousePos)
    {
        int numPoints = line.positionCount;

        for (int i = 0; i < numPoints - 1; i++)
        {
            Vector3 screenPointA = uiCamera.WorldToScreenPoint(line.GetPosition(i));
            Vector3 screenPointB = uiCamera.WorldToScreenPoint(line.GetPosition(i + 1));

            // Check if the mouse is near this segment
            if (DistanceFromMouseToLineSegment(mousePos, screenPointA, screenPointB) < selectionThreshold)
            {
                return true;
            }
        }

        return false;
    }

    // Highlights the line by changing its color
    void HighlightLine(LineRenderer line)
    {
        line.startColor = highlightColor;
        line.endColor = highlightColor;
    }

    // Resets the line color to white (default)
    void ResetLineColor(LineRenderer line)
    {
        line.startColor = Color.white;
        line.endColor = Color.white;
    }

    // Calculates the distance from the mouse position to the closest point on a line segment
    float DistanceFromMouseToLineSegment(Vector2 mousePos, Vector3 pointA, Vector3 pointB)
    {
        Vector2 lineVector = pointB - pointA;
        Vector2 pointVector = mousePos - (Vector2)pointA;

        float t = Mathf.Clamp01(Vector2.Dot(pointVector, lineVector) / lineVector.sqrMagnitude);
        Vector2 projection = (Vector2)pointA + t * lineVector;

        return Vector2.Distance(mousePos, projection);
    }
}
