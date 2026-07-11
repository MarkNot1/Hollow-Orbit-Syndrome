using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class PlayerCamera : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Degrees rotation per pixel of mouse delta (new Input System uses pixel delta).")]
    private float sensitivity = 0.15f;

    [SerializeField]
    private Transform playerBody;

    [SerializeField]
    private PlayerInput playerInput;
    [SerializeField]
    private bool lockCursorOnStart;
    [SerializeField]
    private bool lockCursorOnWorldClick = true;

    private Rigidbody playerBodyRb;
    private float pendingYaw;

    float verticalRotation = 0f;

    private void Awake()
    {
        playerInput = GetComponentInParent<PlayerInput>();
        if (playerBody != null)
            playerBodyRb = playerBody.GetComponent<Rigidbody>();
    }

    private void Start()
    {
        SetCursorLocked(lockCursorOnStart);
    }

    private void Update()
    {
        if (!lockCursorOnWorldClick || Cursor.lockState == CursorLockMode.Locked || Mouse.current == null)
            return;

        if (!Mouse.current.leftButton.wasPressedThisFrame && !Mouse.current.rightButton.wasPressedThisFrame)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        SetCursorLocked(true);
    }

    private void LateUpdate()
    {
        if (playerInput == null) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        // MousePosition is pointer delta (pixels) from new Input System; scale by sensitivity only (no Time.deltaTime).
        float mouseX = playerInput.MousePosition.x * sensitivity;
        float mouseY = playerInput.MousePosition.y * sensitivity;

        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);

        transform.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        pendingYaw += mouseX;
    }

    private void FixedUpdate()
    {
        if (Mathf.Approximately(pendingYaw, 0f))
            return;

        if (playerBodyRb != null)
        {
            Quaternion yawDelta = Quaternion.Euler(0f, pendingYaw, 0f);
            playerBodyRb.MoveRotation(playerBodyRb.rotation * yawDelta);
        }
        else if (playerBody != null)
        {
            playerBody.Rotate(Vector3.up * pendingYaw);
        }

        pendingYaw = 0f;
    }

    public void CaptureCursorForGameplay()
    {
        lockCursorOnStart = true;
        SetCursorLocked(true);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

}

    /*
    private Rigidbody rb;
    private PlayerController playerController;

    private void Start()
    {
        playerController = new PlayerController();
        rb = GetComponent<Rigidbody>();
    }

    public void Jump(InputAction.CallbackContext ctx)
    {
        if (ctx.performed)
        {
            rb.AddForce(Vector3.up * 5f, ForceMode.Impulse);
        }
    }
    */
