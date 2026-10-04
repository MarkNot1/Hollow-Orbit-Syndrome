using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class CircuitComponentHoverDisplay : MonoBehaviour
{
    private const float PopupWidth = 290f;
    private const float CompactHeight = 112f;
    private const float ExpandedHeight = 174f;

    [SerializeField]
    private placementInputManager inputManager;

    [SerializeField]
    private CircuitSimulationSystem simulationSystem;

    [SerializeField]
    private Canvas canvas;

    [SerializeField]
    private RectTransform popup;

    [SerializeField]
    private TextMeshProUGUI popupText;

    [SerializeField]
    private Inventory inventory;

    private PlacedCircuitComponent displayedComponent;
    private ItemSO displayedInventoryItem;
    private bool displayedExpanded;
    private bool contentDirty = true;

    private void OnEnable()
    {
        if (simulationSystem != null)
            simulationSystem.SimulationUpdated += HandleSimulationUpdated;
        else
            Debug.LogError("CircuitSimulationSystem is not assigned to the hover display.", this);

        if (inputManager == null || canvas == null || popup == null || popupText == null)
            Debug.LogError("Circuit hover display references are incomplete.", this);

        if (inventory == null)
            inventory = FindFirstObjectByType<Inventory>();

        // The inventory is a sibling under the same canvas and normally renders after
        // the readout. Keep the readout above it when it is allowed to show.
        popup.SetAsLastSibling();

        SetVisible(false);
    }

    private void OnDisable()
    {
        if (simulationSystem != null)
            simulationSystem.SimulationUpdated -= HandleSimulationUpdated;
        displayedComponent = null;
        displayedInventoryItem = null;
        contentDirty = true;
        SetVisible(false);
    }

    private void Update()
    {
        if (inputManager == null || simulationSystem == null || canvas == null || popup == null || popupText == null ||
            (inputManager.IsPointerOverUI() && !IsInventoryOpen()) ||
            !TryGetDisplayTarget(out PlacedCircuitComponent component, out ItemSO inventoryItem, out CircuitElement inventoryElement))
        {
            SetVisible(false);
            return;
        }

        bool expanded = IsDetailsPressed();
        if (inventoryItem != null)
        {
            if (inventoryItem != displayedInventoryItem || expanded != displayedExpanded || contentDirty)
            {
                displayedComponent = null;
                displayedInventoryItem = inventoryItem;
                displayedExpanded = expanded;
                contentDirty = false;
                UpdateInventoryContent(inventoryItem, inventoryElement, expanded);
            }
        }
        else if (component != displayedComponent || displayedInventoryItem != null ||
                 expanded != displayedExpanded || contentDirty)
        {
            displayedComponent = component;
            displayedInventoryItem = null;
            displayedExpanded = expanded;
            contentDirty = false;
            UpdateContent(component, expanded);
        }

        float popupHeight = expanded ? ExpandedHeight : CompactHeight;
        if (inventoryItem == null && expanded && component.Element.State == CircuitComponentState.Broken)
            popupHeight += 24f;
        popup.sizeDelta = new Vector2(PopupWidth, popupHeight);
        PositionPopup(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
        SetVisible(true);
    }

    private void UpdateContent(PlacedCircuitComponent component, bool expanded)
    {
        CircuitSimulationResult simulation = simulationSystem.CurrentResult;
        string title = string.IsNullOrWhiteSpace(component.Element.EntityName)
            ? component.name
            : component.Element.EntityName;
        string titleText = component.Element.State switch
        {
            CircuitComponentState.Broken =>
                $"<color=#FF9A3C><b>[BROKEN] {title}</b></color>",
            CircuitComponentState.BatteryShorted =>
                $"<color=#FF9A3C><b>[SHORTED] {title}</b></color>",
            _ => $"<b>{title}</b>"
        };
        CircuitNode terminal1 = component.Element.Terminal1;
        CircuitNode terminal2 = component.Element.Terminal2;
        const string detailsBinding = "CTRL";
        string expandedFailureReason =
            component.Element.State == CircuitComponentState.Broken &&
            !string.IsNullOrWhiteSpace(component.Element.FailureReason)
                ? $"\n<color=#FF9A3C>{component.Element.FailureReason}</color>"
                : string.Empty;

        if (simulation != null && simulation.Success &&
            simulation.Elements.TryGetValue(component.Element, out CircuitElementResult result))
        {
            double terminal1Voltage = simulation.GetNodeVoltage(terminal1);
            double terminal2Voltage = simulation.GetNodeVoltage(terminal2);
            string compact = $"{titleText}  [{terminal1.TerminalLabel} to {terminal2.TerminalLabel}]\n" +
                $"Final voltage: {FormatVoltage(terminal2Voltage)}\n" +
                $"Current: {FormatCurrent(result.Current)}\n" +
                $"<color=#AAB6C8>Hold {detailsBinding} to expand</color>";

            popupText.text = expanded
                ? compact + "\n" +
                    $"Input {terminal1.TerminalLabel} ({terminal1.NetName}): {FormatVoltage(terminal1Voltage)}\n" +
                    $"Output {terminal2.TerminalLabel} ({terminal2.NetName}): {FormatVoltage(terminal2Voltage)}\n" +
                    $"Voltage drop: {FormatVoltage(result.Voltage)}" +
                    expandedFailureReason
                : compact;
        }
        else
        {
            string compact = $"{titleText}  [{terminal1.TerminalLabel} to {terminal2.TerminalLabel}]\n" +
                $"Final voltage: --\nCurrent: --\n" +
                $"<color=#AAB6C8>Hold {detailsBinding} to expand</color>";
            popupText.text = expanded ? compact + expandedFailureReason : compact;
        }
    }

    private void UpdateInventoryContent(ItemSO item, CircuitElement element, bool expanded)
    {
        string title = string.IsNullOrWhiteSpace(item.itemName) ? element.Kind.ToString() : item.itemName;
        string valueLabel = element.Kind switch
        {
            CircuitElementKind.Battery => "Voltage",
            CircuitElementKind.Capacitor => "Capacitance",
            _ => "Value"
        };
        string value = element.Kind == CircuitElementKind.Capacitor
            ? FormatCapacitance(element.Value)
            : element.Kind == CircuitElementKind.Battery
                ? FormatVoltage(element.Value)
                : FormatResistance(element.Value);

        popupText.text = $"<b>{title}</b>  [{element.Kind}]\n" +
            $"{valueLabel}: {value}\n" +
            (expanded
                ? $"Max voltage: {FormatRating(element.MaximumVoltage, "V")}\n" +
                  $"Max current: {FormatRating(element.MaximumCurrent, "A")}\n" +
                  $"Max power: {FormatRating(element.MaximumPower, "W")}"
                : "<color=#AAB6C8>Hold CTRL to expand</color>");
    }

    private bool TryGetDisplayTarget(
        out PlacedCircuitComponent component,
        out ItemSO inventoryItem,
        out CircuitElement inventoryElement)
    {
        component = null;
        inventoryItem = null;
        inventoryElement = null;

        if (IsInventoryOpen() && inventory != null &&
            inventory.TryGetHoveredCircuitComponent(out inventoryItem, out inventoryElement))
            return true;

        if (!inputManager.TryGetCircuitComponentBody(out component) || component?.Element == null)
            return false;

        return true;
    }

    private static string FormatRating(double value, string unit)
        => value > 0d ? $"{value:0.###} {unit}" : "--";

    private static string FormatCapacitance(double value)
    {
        double magnitude = System.Math.Abs(value);
        if (magnitude >= 1e-3d)
            return $"{value * 1e3d:0.###} mF";
        if (magnitude >= 1e-6d)
            return $"{value * 1e6d:0.###} uF";
        if (magnitude >= 1e-9d)
            return $"{value * 1e9d:0.###} nF";
        return $"{value * 1e12d:0.###} pF";
    }

    private static string FormatResistance(double value)
        => $"{value:0.###} ohm";

    private void PositionPopup(Vector2 screenPosition)
    {
        RectTransform canvasRect = canvas.transform as RectTransform;
        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (canvasRect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPosition, eventCamera, out Vector2 localPoint))
            return;

        Vector2 scale = canvasRect.rect.size;
        Vector2 position = localPoint + new Vector2(18f, -18f);
        float left = -scale.x * 0.5f;
        float right = scale.x * 0.5f - PopupWidth;
        float bottom = -scale.y * 0.5f + popup.rect.height;
        float top = scale.y * 0.5f;
        popup.anchoredPosition = new Vector2(
            Mathf.Clamp(position.x, left, right),
            Mathf.Clamp(position.y, bottom, top));
    }

    private void SetVisible(bool visible)
    {
        if (popup != null && popup.gameObject.activeSelf != visible)
            popup.gameObject.SetActive(visible);
    }

    private static string FormatVoltage(double value)
    {
        double magnitude = System.Math.Abs(value);
        return magnitude < 1d
            ? $"{value * 1000d:0.###} mV"
            : $"{value:0.###} V";
    }

    private bool IsDetailsPressed()
    {
        return Keyboard.current != null &&
            (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
    }

    private bool IsInventoryOpen()
        => inventory != null && inventory.IsOpen;

    private void HandleSimulationUpdated(CircuitSimulationResult _)
        => contentDirty = true;

    private static string FormatCurrent(double value)
    {
        double magnitude = System.Math.Abs(value);
        if (magnitude >= 1d)
            return $"{value:0.###} A";
        if (magnitude >= 1e-3d)
            return $"{value * 1e3d:0.###} mA";
        if (magnitude >= 1e-6d)
            return $"{value * 1e6d:0.###} uA";
        return $"{value * 1e9d:0.###} nA";
    }
}
