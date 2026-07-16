using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class placementInputManager : MonoBehaviour
{
    [SerializeField] 
    private Camera sceneCamera;

    private Vector3 lastPosition;

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
