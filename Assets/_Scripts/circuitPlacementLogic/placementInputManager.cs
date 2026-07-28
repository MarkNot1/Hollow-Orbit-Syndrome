using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class placementInputManager : MonoBehaviour
{
    private const string CircuitPlacementMapName = "CircuitPlacement";
    private const string RotateComponentActionName = "RotateComponent";

    [SerializeField] 
    private Camera sceneCamera;

    [SerializeField]
    private InputActionAsset inputActions;

    private Vector3 lastPosition;
    private readonly RaycastHit[] raycastHits = new RaycastHit[32];
    private InputActionMap circuitPlacementMap;
    private InputAction rotateComponentAction;

    public event Action OnClicked, OnRightClicked, OnExit, OnRotate;

    private void Awake()
    {
        if (sceneCamera == null)
            sceneCamera = Camera.main;

        ConfigurePlacementActions();
    }

    private void OnEnable()
    {
        ConfigurePlacementActions();
        if (rotateComponentAction != null)
            rotateComponentAction.performed += HandleRotatePerformed;
        circuitPlacementMap?.Enable();
    }

    private void OnDisable()
    {
        if (rotateComponentAction != null)
            rotateComponentAction.performed -= HandleRotatePerformed;
        circuitPlacementMap?.Disable();
    }

    void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            OnClicked?.Invoke();
        }
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            OnRightClicked?.Invoke();
        }
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            OnExit?.Invoke();
        }
    }

    private void ConfigurePlacementActions()
    {
        if (circuitPlacementMap != null)
            return;

        if (inputActions == null)
        {
            Debug.LogError(
                "PlacementInputManager: Assign the InputSystem_Actions asset in the Inspector.",
                this);
            return;
        }

        circuitPlacementMap = inputActions.FindActionMap(CircuitPlacementMapName);
        rotateComponentAction = circuitPlacementMap?.FindAction(RotateComponentActionName);

        if (rotateComponentAction == null)
        {
            Debug.LogError(
                $"PlacementInputManager: Input actions must contain " +
                $"'{CircuitPlacementMapName}/{RotateComponentActionName}'.",
                this);
        }
    }

    private void HandleRotatePerformed(InputAction.CallbackContext _)
        => OnRotate?.Invoke();

    public bool IsPointerOverUI()
        => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    public bool TryGetSelectedPositionOnPlane(Plane plane, out Vector3 position)
    {
        position = lastPosition;
        if (sceneCamera == null || Mouse.current == null)
            return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = sceneCamera.ScreenPointToRay(mousePos);
        if (!plane.Raycast(ray, out float distance))
            return false;

        position = ray.GetPoint(distance);
        lastPosition = position;
        return true;
    }

    public bool TryGetPointerRay(out Ray ray)
    {
        ray = default;
        if (sceneCamera == null || Mouse.current == null)
            return false;

        ray = sceneCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        return true;
    }

    public bool TryGetRaycastComponent<T>(LayerMask mask, out T component, float distance = 100f)
        where T : Component
    {
        component = null;
        if (!TryGetPointerRay(out Ray ray))
            return false;

        int count = Physics.RaycastNonAlloc(
            ray,
            raycastHits,
            distance,
            mask,
            QueryTriggerInteraction.Collide);
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            T candidate = raycastHits[i].collider.GetComponentInParent<T>();
            if (candidate != null && raycastHits[i].distance < closestDistance)
            {
                closestDistance = raycastHits[i].distance;
                component = candidate;
            }
        }

        return component != null;
    }

    public bool TryGetCircuitComponentBody(out PlacedCircuitComponent component, float distance = 100f)
    {
        component = null;
        if (!TryGetPointerRay(out Ray ray))
            return false;

        int count = Physics.RaycastNonAlloc(
            ray,
            raycastHits,
            distance,
            ~0,
            QueryTriggerInteraction.Collide);
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Collider hitCollider = raycastHits[i].collider;
            if (hitCollider.GetComponentInParent<CircuitNode>() != null)
                continue;

            PlacedCircuitComponent candidate =
                hitCollider.GetComponentInParent<PlacedCircuitComponent>();
            if (candidate != null && raycastHits[i].distance < closestDistance)
            {
                closestDistance = raycastHits[i].distance;
                component = candidate;
            }
        }

        return component != null;
    }

}
