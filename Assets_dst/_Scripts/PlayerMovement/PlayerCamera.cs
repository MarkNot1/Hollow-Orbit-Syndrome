using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Degrees rotation per pixel of mouse delta (new Input System uses pixel delta).")]
    private float sensitivity = 0.15f;

    [SerializeField]
    private Transform playerBody;

    [SerializeField]
    private PlayerInput playerInput;

    float verticalRotation = 0f;

    private void Awake()
    {
        playerInput = GetComponentInParent<PlayerInput>();
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        if (playerInput == null) return;
        // MousePosition is pointer delta (pixels) from new Input System; scale by sensitivity only (no Time.deltaTime).
        float mouseX = playerInput.MousePosition.x * sensitivity;
        float mouseY = playerInput.MousePosition.y * sensitivity;

        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);

        transform.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        if (playerBody != null)
            playerBody.Rotate(Vector3.up * mouseX);
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

