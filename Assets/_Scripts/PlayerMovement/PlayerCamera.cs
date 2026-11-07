using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-10)]
public class PlayerCamera : MonoBehaviour
{
    [SerializeField, Min(0f)]
    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    private float sensitivity = 0.12f;
    [SerializeField, Min(0f)]
    [Tooltip("Look smoothing in seconds. Zero disables smoothing.")]
    private float lookSmoothTime = 0.04f;
    [SerializeField] private Transform playerBody;
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private bool lockCursorOnStart;
    [SerializeField] private bool lockCursorOnWorldClick = true;

    private Rigidbody playerBodyRb;
    private Vector2 targetAngles, lookAngles, lookVelocity;
    private bool CanLook => playerInput != null && !playerInput.IsGameplayInputBlocked &&
                            Cursor.lockState == CursorLockMode.Locked;

    private void Awake()
    {
        playerInput = GetComponentInParent<PlayerInput>();
        if (playerBody != null) playerBodyRb = playerBody.GetComponent<Rigidbody>();
    }

    private void OnEnable() => ResetLook();
    private void OnDisable() => ResetLook();
    private void Start() => SetCursorLocked(lockCursorOnStart);

    private void Update()
    {
        if (playerInput == null || playerInput.IsGameplayInputBlocked || !lockCursorOnWorldClick ||
            Cursor.lockState == CursorLockMode.Locked || Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame && !Mouse.current.rightButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        SetCursorLocked(true);
    }

    private void LateUpdate()
    {
        if (!CanLook) { ResetLook(); return; }
        UpdateLook(playerInput.MousePosition, Time.unscaledDeltaTime);
    }

    private void UpdateLook(Vector2 mouseDelta, float deltaTime)
    {
        if (deltaTime <= 0f) return;
        // Pointer delta already measures this frame's movement; only damping uses time.
        targetAngles.x = Mathf.Clamp(targetAngles.x - mouseDelta.y * sensitivity, -90f, 90f);
        targetAngles.y = Mathf.Repeat(targetAngles.y + mouseDelta.x * sensitivity, 360f);
        if (lookSmoothTime <= 0f)
        {
            lookAngles = targetAngles;
            lookVelocity = Vector2.zero;
        }
        else
        {
            lookAngles.x = Mathf.SmoothDamp(lookAngles.x, targetAngles.x, ref lookVelocity.x,
                lookSmoothTime, Mathf.Infinity, deltaTime);
            lookAngles.y = Mathf.Repeat(Mathf.SmoothDampAngle(lookAngles.y, targetAngles.y, ref lookVelocity.y,
                lookSmoothTime, Mathf.Infinity, deltaTime), 360f);
        }
        // World rotation keeps rendered look independent of the body's physics ticks.
        transform.rotation = Quaternion.Euler(lookAngles.x, lookAngles.y, 0f);
    }

    private void FixedUpdate()
    {
        if (!CanLook) return;
        Quaternion rotation = Quaternion.Euler(0f, lookAngles.y, 0f);
        if (playerBodyRb != null) playerBodyRb.MoveRotation(rotation);
        else if (playerBody != null) playerBody.rotation = rotation;
    }

    private void ResetLook()
    {
        Vector3 angles = transform.eulerAngles;
        targetAngles = lookAngles = new Vector2(Mathf.DeltaAngle(0f, angles.x), angles.y);
        lookVelocity = Vector2.zero;
    }

    public void CaptureCursorForGameplay()
    {
        lockCursorOnStart = true;
        ResetLook();
        SetCursorLocked(true);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
