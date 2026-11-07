using System.Collections;
using UnityEngine;

public class Character : MonoBehaviour
{
    [SerializeField]
    private PlayerInput playerInput;
    [SerializeField]
    private PlayerMovement playerMovement;
    [SerializeField]
    private PlayerPlacementController placementController;

    public bool fly = false;

    public Animator animator;

    bool isWaiting = false;

    public World world;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        playerMovement = GetComponent<PlayerMovement>();
        placementController = GetComponent<PlayerPlacementController>();
        if (placementController == null)
            placementController = gameObject.AddComponent<PlayerPlacementController>();
        world = FindFirstObjectByType<World>();
    }

    private void Start()
    {
        playerInput.OnDestroyVoxel += TryDeleteTargetVoxel;
        playerInput.OnPlaceVoxel += TryPlaceEquippedVoxel;
        playerInput.OnFly += HandleFlyClick;
    }

    private void OnDestroy()
    {
        if (playerInput != null)
        {
            playerInput.OnDestroyVoxel -= TryDeleteTargetVoxel;
            playerInput.OnPlaceVoxel -= TryPlaceEquippedVoxel;
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

    private void TryDeleteTargetVoxel()
    {
        if (!playerInput.TryGetReticleHit(out RaycastHit hit)) return;
        StructureAttachment attachment = hit.collider.GetComponentInParent<StructureAttachment>();
        if (attachment != null)
        {
            attachment.GetComponentInParent<VoxelStructure>()?.RemoveAttachment(attachment);
            return;
        }
        VoxelStructure structure = hit.collider.GetComponent<StructureChunk>()?.Structure;
        if (structure != null && structure.TryGetHitCell(hit, out var surface, out _))
            structure.SetVoxel(surface, VoxelType.Air);
        else if (world != null && hit.collider.GetComponent<ChunkRenderer>() != null)
            world.SetVoxel(hit, VoxelType.Air);
    }

    private void TryPlaceEquippedVoxel()
    {
        if (placementController == null)
            return;

        placementController.TryPlaceEquippedVoxel();
    }
}
