using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class MultiplayerPlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 8f;
    public float mouseSensitivity = 2f;
    public float gravity = -20f;

    [Header("References")]
    public Camera playerCamera;

    private CharacterController controller;
    private PlayerItemController itemController;

    private float verticalVelocity;
    private float cameraPitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        itemController = GetComponent<PlayerItemController>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            if (playerCamera != null)
                playerCamera.enabled = false;

            AudioListener listener = GetComponentInChildren<AudioListener>();
            if (listener != null)
                listener.enabled = false;

            enabled = false;
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (playerCamera != null)
            playerCamera.enabled = true;
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        HandleLook();
        HandleMovement();
    }

    private void HandleLook()
    {
        if (playerCamera == null)
            return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -80f, 80f);

        playerCamera.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    private void HandleMovement()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        bool isCranking = itemController != null && itemController.IsCranking;
        bool isSprinting = Input.GetKey(KeyCode.LeftShift) && !isCranking;

        float currentSpeed = isSprinting ? sprintSpeed : moveSpeed;

        float speedMultiplier = 1f;

        if (itemController != null)
            speedMultiplier = itemController.CrankingMoveMultiplier;

        Vector3 horizontalMove = transform.right * x + transform.forward * z;
        horizontalMove *= currentSpeed * speedMultiplier;

        if (controller.isGrounded && verticalVelocity < 0)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontalMove;
        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }
}