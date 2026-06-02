using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Character : MonoBehaviour
{
    [SerializeField]
    private Camera mainCamera;
    [SerializeField]
    private PlayerInput playerInput;
    [SerializeField]
    private PlayerMovement playerMovement;
    [SerializeField]
    private PlayerPlacementController placementController;

    public float interactionRayLength = 5;

    public bool fly = false;

    public Animator animator;

    bool isWaiting = false;

    public World world;

    private static readonly RaycastHit[] RaycastHitsNonAlloc = new RaycastHit[32];

    private void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;
        playerInput = GetComponent<PlayerInput>();
        playerMovement = GetComponent<PlayerMovement>();
        placementController = GetComponent<PlayerPlacementController>();
        if (placementController == null)
            placementController = gameObject.AddComponent<PlayerPlacementController>();
        placementController.InteractionRayLength = interactionRayLength;
        world = FindFirstObjectByType<World>();
    }

    private void Start()
    {
        playerInput.OnDestroyBlock += TryDeleteTargetBlock;
        playerInput.OnPlaceBlock += TryPlaceSelectedItem;
        playerInput.OnFly += HandleFlyClick;
    }

    private void OnDestroy()
    {
        if (playerInput != null)
        {
            playerInput.OnDestroyBlock -= TryDeleteTargetBlock;
            playerInput.OnPlaceBlock -= TryPlaceSelectedItem;
            playerInput.OnFly -= HandleFlyClick;
        }
    }

    private void HandleFlyClick()
    {
        fly = !fly;
    }

    void FixedUpdate()
    {
        playerMovement.RefreshGroundState();

        if (fly)
        {
            playerMovement.Fly(playerInput.MovementInput, playerInput.IsJumping, playerInput.RunningPressed);
        }
        else
        {
            if (playerMovement.IsGrounded && playerInput.IsJumping && isWaiting == false)
            {
                isWaiting = true;
                StopAllCoroutines();
                StartCoroutine(ResetWaiting());
            }

            playerMovement.HandleGravity(playerInput.IsJumping);
            playerMovement.Walk(playerInput.MovementInput, playerInput.RunningPressed);
        }
    }

    private void OnGUI()
    {
        // Simple crosshair in the middle of the screen
        float size = 4f;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(Screen.width / 2f - size / 2f, Screen.height / 2f - size / 2f, size, size), Texture2D.whiteTexture);
    }

    IEnumerator ResetWaiting()
    {
        yield return new WaitForSeconds(0.1f);
        isWaiting = false;
    }

    private void TryDeleteTargetBlock()
    {
        if (world == null || mainCamera == null)
            return;

        Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);
        int hitCount = Physics.RaycastNonAlloc(ray, RaycastHitsNonAlloc, interactionRayLength, ~0, QueryTriggerInteraction.Ignore);
        if (hitCount <= 0)
            return;

        int bestIndex = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit h = RaycastHitsNonAlloc[i];
            if (h.collider != null && h.collider.GetComponent<ChunkRenderer>() != null && h.distance < bestDistance)
            {
                bestDistance = h.distance;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0)
            world.SetVoxel(RaycastHitsNonAlloc[bestIndex], VoxelType.Air);
    }

    private void TryPlaceSelectedItem()
    {
        if (placementController == null)
            return;

        placementController.InteractionRayLength = interactionRayLength;
        placementController.TryPlaceSelectedItem();
    }
}
