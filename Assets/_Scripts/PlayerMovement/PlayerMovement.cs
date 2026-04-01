using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField]
    private Rigidbody rb;

    [SerializeField]
    private float playerSpeed = 5.0f, playerRunSpeed = 8;
    [SerializeField]
    private float jumpHeight = 1.0f;
    [SerializeField]
    private float gravityValue = -9.81f;
    [SerializeField]
    private float flySpeed = 2;

    private Vector3 playerVelocity;

    [Header("Grounded check parameters:")]
    [SerializeField]
    private float rayDistance = 1;
    [field: SerializeField]
    public bool IsGrounded { get; private set; }
    private PhysicsMaterial glideMaterial;

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false; // we apply gravity in HandleGravity to match previous behaviour
            rb.freezeRotation = true;   // prevent tipping over
            rb.interpolation = RigidbodyInterpolation.Interpolate; // smoother movement
        }

        Collider playerCollider = GetComponent<Collider>();
        if (playerCollider != null)
        {
            glideMaterial = new PhysicsMaterial("PlayerGlide")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            playerCollider.sharedMaterial = glideMaterial;
        }
    }

    private Vector3 GetMovementDirection(Vector3 movementInput)
    {
        return transform.right * movementInput.x + transform.forward * movementInput.z;
    }

    public void Fly(Vector3 movementInput, bool ascendInput, bool descendInput)
    {
        if (rb == null) return;

        Vector3 movementDirection = GetMovementDirection(movementInput);

        if (ascendInput)
            movementDirection += Vector3.up * flySpeed;
        else if (descendInput)
            movementDirection -= Vector3.up * flySpeed;

        rb.linearVelocity = movementDirection * playerSpeed;
    }

    public void Walk(Vector3 movementInput, bool runningInput)
    {
        if (rb == null) return;

        Vector3 movementDirection = GetMovementDirection(movementInput);
        float speed = runningInput ? playerRunSpeed : playerSpeed;
        Vector3 horizontalVelocity = movementDirection * speed;

        rb.linearVelocity = new Vector3(horizontalVelocity.x, playerVelocity.y, horizontalVelocity.z);
    }

    public void HandleGravity(bool isJumping)
    {
        if (rb == null) return;

        if (IsGrounded && playerVelocity.y < 0)
            playerVelocity.y = 0f;

        if (isJumping && IsGrounded)
            AddJumpForce();

        ApplyGravityForce();

        // keep current horizontal velocity, only change vertical
        Vector3 vel = rb.linearVelocity;
        rb.linearVelocity = new Vector3(vel.x, playerVelocity.y, vel.z);
    }

    private void AddJumpForce()
    {
        playerVelocity.y = jumpHeight;
    }

    private void ApplyGravityForce()
    {
        playerVelocity.y += gravityValue * Time.deltaTime;
        playerVelocity.y = Mathf.Clamp(playerVelocity.y, gravityValue, 10);
    }

    private void Update()
    {
        IsGrounded = Physics.Raycast(transform.position, Vector3.down, rayDistance, ~0, QueryTriggerInteraction.Ignore);
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawRay(transform.position, Vector3.down * rayDistance);
    }
}
