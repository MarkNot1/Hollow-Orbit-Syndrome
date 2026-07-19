
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(CanvasGroup))]
public class DraggableComponent : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{

    RectTransform componentRect;
    RectTransform BoundariesRect;
    Vector2 InitialPosition;
    private CanvasGroup canvasGroup;
    private Canvas canvas;


    void Awake()
    {
        componentRect = GetComponent<RectTransform>();
        BoundariesRect = GetComponentInParent<Canvas>().GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        canvas = GetComponentInParent<Canvas>();
        InitialPosition = componentRect.anchoredPosition;
    }
  
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (Circuit_ConnectionManager.Instance.AddingWires || Circuit_ConnectionManager.Instance.AddingNodes || Circuit_ConnectionManager.Instance.currentHoveredNode != null || WireDrawingWithGrid.Instance.isDrawing)
            return;
        canvasGroup.alpha = 0.6f;
        canvasGroup.blocksRaycasts = false;
      
    }
    public void OnEndDrag(PointerEventData eventData)
    {
        if (Circuit_ConnectionManager.Instance.AddingWires || Circuit_ConnectionManager.Instance.AddingNodes || Circuit_ConnectionManager.Instance.currentHoveredNode != null || WireDrawingWithGrid.Instance.isDrawing)
            return;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        componentRect.anchoredPosition = Circuit_Utilities.SnapToGrid2D(componentRect.anchoredPosition);
    }
    public void OnDrag(PointerEventData eventData)
    {
        if (Circuit_ConnectionManager.Instance.AddingWires || Circuit_ConnectionManager.Instance.AddingNodes || Circuit_ConnectionManager.Instance.currentHoveredNode != null || WireDrawingWithGrid.Instance.isDrawing)
            return;
        //ElectronsSimulatorManager.Instance.ResetAllSimulators();
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            canvas.worldCamera,
            out localPoint
        );

        componentRect.anchoredPosition = Circuit_Utilities.SnapToGrid2D(localPoint);

    }


}
