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
    [Tooltip("How far below the probe we search for surfaces (meters). Long casts can hit geometry far below; see Ground snap distance.")]
    private float rayDistance = 0.35f;
    [SerializeField]
    [Tooltip("Start the cast slightly above the collider bottom to avoid starting inside floor mesh.")]
    private float groundProbeSkin = 0.06f;
    [SerializeField]
    [Tooltip("Only treat as grounded (via ray) if the closest hit is within this distance from the probe origin. Keep small so we do not \"rest\" in mid-air.")]
    private float groundSnapDistance = 0.14f;
    [field: SerializeField]
    public bool IsGrounded { get; private set; }
    private PhysicsMaterial glideMaterial;
    private Collider selfCollider;
    /// <summary>Set during the last physics step if a solid contact supports the player from below.</summary>
    private bool physicsGroundedLatch;
    private static readonly RaycastHit[] GroundHits = new RaycastHit[12];

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

        selfCollider = GetComponent<Collider>();
        if (selfCollider != null)
        {
            glideMaterial = new PhysicsMaterial("PlayerGlide")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            selfCollider.sharedMaterial = glideMaterial;
        }
    }

    /// <summary>
    /// Call from <see cref="FixedUpdate"/> before jump/gravity so <see cref="IsGrounded"/> matches this physics step.
    /// Any non-trigger collider below the feet counts as ground (full layer mask).
    /// </summary>
    public void RefreshGroundState()
    {
        bool fromPhysics = physicsGroundedLatch;
        physicsGroundedLatch = false;
        IsGrounded = ComputeGrounded() || fromPhysics;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision.collider == null || collision.collider.isTrigger)
            return;
        if (selfCollider != null && collision.collider == selfCollider)
            return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 n = collision.GetContact(i).normal;
            if (Vector3.Dot(n, Vector3.up) > 0.25f)
            {
                physicsGroundedLatch = true;
                return;
            }
        }
    }

    private bool ComputeGrounded()
    {
        const int mask = ~0;

        if (selfCollider == null)
            return Physics.Raycast(transform.position, Vector3.down, rayDistance, mask, QueryTriggerInteraction.Ignore);

        float skin = Mathf.Max(0.01f, groundProbeSkin);
        Vector3 origin = new Vector3(selfCollider.bounds.center.x, selfCollider.bounds.min.y + skin, selfCollider.bounds.center.z);
        float maxDist = skin + rayDistance;

        int n = Physics.RaycastNonAlloc(origin, Vector3.down, GroundHits, maxDist, mask, QueryTriggerInteraction.Ignore);
        float closest = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = GroundHits[i];
            Collider c = h.collider;
            if (c == null || c == selfCollider || c.isTrigger)
                continue;
            if (h.distance < closest)
                closest = h.distance;
        }

        return closest <= groundSnapDistance;
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

        if (isJumping && IsGrounded)
            AddJumpForce();

        // When supported, do not apply gravity — a tiny downward vy each frame was fighting the solver and caused hover / slow settle.
        if (IsGrounded && playerVelocity.y <= 0f)
            playerVelocity.y = 0f;
        else
            ApplyGravityForce();

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

    private void OnDrawGizmos()
    {
        Collider c = selfCollider != null ? selfCollider : GetComponent<Collider>();
        if (c != null)
        {
            float skin = Mathf.Max(0.01f, groundProbeSkin);
            Vector3 o = new Vector3(c.bounds.center.x, c.bounds.min.y + skin, c.bounds.center.z);
            float castLen = skin + rayDistance;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(o, Vector3.down * castLen);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(o, Vector3.down * Mathf.Min(castLen, groundSnapDistance));
        }
        else
            Gizmos.DrawRay(transform.position, Vector3.down * rayDistance);
    }
}
