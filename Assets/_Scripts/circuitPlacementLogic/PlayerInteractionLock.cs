using System.Collections;
using UnityEngine;

public class PlayerInteractionLock : MonoBehaviour
{
    [SerializeField]
    private PlayerInput playerInput;
    [SerializeField]
    private Character character;
    [SerializeField]
    private PlayerCamera playerCamera;
    [SerializeField]
    private Rigidbody playerRigidbody;

    private Coroutine cursorRestoreCoroutine;

    private void Awake()
    {
        if (playerInput == null)
            playerInput = GetComponent<PlayerInput>();
        if (character == null)
            character = GetComponent<Character>();
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<PlayerCamera>();
        if (playerRigidbody == null)
            playerRigidbody = GetComponent<Rigidbody>();
    }

    public void SetLocked(bool locked)
    {
        if (playerInput != null)
            playerInput.SetGameplayInputBlocked(locked);

        if (playerRigidbody != null)
        {
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
        }

        if (character != null)
            character.enabled = !locked;
        if (playerCamera != null)
            playerCamera.enabled = !locked;

        Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = locked;

        if (!locked)
            RestoreCursorLockEndOfFrame();
    }

    private void RestoreCursorLockEndOfFrame()
    {
        if (cursorRestoreCoroutine != null)
            StopCoroutine(cursorRestoreCoroutine);

        cursorRestoreCoroutine = StartCoroutine(RestoreLockedCursor());
    }

    private IEnumerator RestoreLockedCursor()
    {
        yield return new WaitForEndOfFrame();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cursorRestoreCoroutine = null;
    }
}
