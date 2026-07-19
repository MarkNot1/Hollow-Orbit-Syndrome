using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class placementInputManager : MonoBehaviour
{
    [SerializeField] 
    private Camera sceneCamera;

    private Vector3 lastPosition;
    private readonly RaycastHit[] raycastHits = new RaycastHit[32];

    [SerializeField]
    LayerMask placementLayerMask;

    public event Action OnClicked, OnRightClicked, OnExit;

    private void Awake()
    {
        if (sceneCamera == null)
            sceneCamera = Camera.main;
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

    public Vector3 GetSelectedMapPosition()
    {
        if (sceneCamera == null || Mouse.current == null)
            return lastPosition;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = sceneCamera.ScreenPointToRay(mousePos);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 100, placementLayerMask))
        {
            lastPosition = hit.point;
        }
        return lastPosition;
    }
}
