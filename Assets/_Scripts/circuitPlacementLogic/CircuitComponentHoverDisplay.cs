using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class CircuitComponentHoverDisplay : MonoBehaviour
{
    private const float PopupWidth = 290f;
    private const float CompactHeight = 112f;
    private const float ExpandedHeight = 174f;

    private placementInputManager inputManager;
    private CircuitSimulationSystem simulationSystem;
    private PlayerInput playerInput;
    private Canvas canvas;
    private RectTransform popup;
    private TextMeshProUGUI popupText;

    public void Configure(placementInputManager input, CircuitSimulationSystem simulation)
    {
        inputManager = input;
        simulationSystem = simulation;
        playerInput = FindFirstObjectByType<PlayerInput>();
    }

    private void Update()
    {
        if (inputManager == null || simulationSystem?.CurrentTopology == null ||
            inputManager.IsPointerOverUI() ||
            !inputManager.TryGetCircuitComponentBody(out PlacedCircuitComponent component) ||
            component?.Element == null)
        {
            SetVisible(false);
            return;
        }

        EnsurePopup();
        CircuitSimulationResult simulation = simulationSystem.CurrentResult;
        string title = string.IsNullOrWhiteSpace(component.Element.EntityName)
            ? component.name
            : component.Element.EntityName;
        CircuitNode terminal1 = component.Element.Terminal1;
        CircuitNode terminal2 = component.Element.Terminal2;
        bool expanded = IsDetailsPressed();
        string detailsBinding = GetDetailsBindingName();

        if (simulation != null && simulation.Success &&
            simulation.Elements.TryGetValue(component.Element, out CircuitElementResult result))
        {
            double terminal1Voltage = GetNodeVoltage(simulation, terminal1.SpiceNodeName);
            double terminal2Voltage = GetNodeVoltage(simulation, terminal2.SpiceNodeName);
            string compact = $"<b>{title}</b>  [{terminal1.TerminalLabel} to {terminal2.TerminalLabel}]\n" +
                $"Final voltage: {FormatVoltage(terminal2Voltage)}\n" +
                $"Current: {FormatCurrent(result.Current)}\n" +
                $"<color=#AAB6C8>Hold {detailsBinding} to expand</color>";

            popupText.text = expanded
                ? compact + "\n" +
                    $"Input {terminal1.TerminalLabel} ({terminal1.NetName}): {FormatVoltage(terminal1Voltage)}\n" +
                    $"Output {terminal2.TerminalLabel} ({terminal2.NetName}): {FormatVoltage(terminal2Voltage)}\n" +
                    $"Voltage drop: {FormatVoltage(result.Voltage)}"
                : compact;
        }
        else
        {
            popupText.text = $"<b>{title}</b>  [{terminal1.TerminalLabel} to {terminal2.TerminalLabel}]\n" +
                $"Final voltage: --\nCurrent: --\n" +
                $"<color=#AAB6C8>Hold {detailsBinding} to expand</color>";
        }

        popup.sizeDelta = new Vector2(PopupWidth, expanded ? ExpandedHeight : CompactHeight);
        PositionPopup(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
        SetVisible(true);
    }

    private void EnsurePopup()
    {
        if (popup != null)
            return;

        canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new("Circuit Hover Canvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
        }

        GameObject popupObject = new("Circuit Component Readout", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(CanvasGroup));
        popupObject.transform.SetParent(canvas.transform, false);
        popup = popupObject.GetComponent<RectTransform>();
        popup.anchorMin = popup.anchorMax = new Vector2(0.5f, 0.5f);
        popup.pivot = new Vector2(0f, 1f);
        popup.sizeDelta = new Vector2(PopupWidth, CompactHeight);

        Image background = popupObject.GetComponent<Image>();
        background.color = new Color(0.035f, 0.045f, 0.06f, 0.94f);
        background.raycastTarget = false;
        CanvasGroup group = popupObject.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        GameObject textObject = new("Values", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(popup, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 8f);
        textRect.offsetMax = new Vector2(-12f, -8f);
        popupText = textObject.GetComponent<TextMeshProUGUI>();
        popupText.fontSize = 17f;
        popupText.color = Color.white;
        popupText.alignment = TextAlignmentOptions.MidlineLeft;
        popupText.raycastTarget = false;
        popupText.textWrappingMode = TextWrappingModes.NoWrap;
        popup.SetAsLastSibling();
    }

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

    private static double GetNodeVoltage(CircuitSimulationResult simulation, string spiceNodeName)
        => spiceNodeName == "0"
            ? 0d
            : simulation.NodeVoltages.TryGetValue(spiceNodeName, out double voltage) ? voltage : 0d;

    private bool IsDetailsPressed()
    {
        if (playerInput == null)
            playerInput = FindFirstObjectByType<PlayerInput>();
        if (playerInput != null && playerInput.IsCircuitDetailsPressed())
            return true;

        return Keyboard.current != null &&
            (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
    }

    private string GetDetailsBindingName()
    {
        if (playerInput == null)
            playerInput = FindFirstObjectByType<PlayerInput>();
        return playerInput != null ? playerInput.GetCircuitDetailsBindingDisplayName() : "CTRL";
    }

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
