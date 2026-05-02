using TMPro;
using Unity.Netcode;
using UnityEngine;

public class PlayerItemController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform holdPoint;
    [SerializeField] private TMP_Text pickupPromptText;
    [SerializeField] private CrankMiniGameUI crankMiniGameUI;

    [Header("Pickup")]
    [SerializeField] private float pickupRange = 3f;
    [SerializeField] private LayerMask itemMask;

    private TorchItem heldTorch;
    private TorchItem lookedAtTorch;

    public bool IsCranking => crankMiniGameUI != null && crankMiniGameUI.IsActive;
    private bool wasHoldingCrank;

    public float CrankingMoveMultiplier => IsCranking ? 0.5f : 1f;

    public Transform GetHoldPoint() => holdPoint;

    private void Start()
    {
        if (pickupPromptText != null)
            pickupPromptText.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        if (crankMiniGameUI != null && crankMiniGameUI.IsActive)
        {
            HandleCrankInput();
            return;
        }

        CheckForPickupPrompt();

        if (Input.GetKeyDown(KeyCode.E) && lookedAtTorch != null)
            PickupTorch();

        if (Input.GetKeyDown(KeyCode.F) && heldTorch != null)
            heldTorch.TryToggle();

        if (Input.GetKeyDown(KeyCode.Q) && heldTorch != null)
            DropTorch();

        if (Input.GetKeyDown(KeyCode.R) && heldTorch != null)
        {
            StartCrankMiniGame();
            wasHoldingCrank = true;
        }
    }

    private void CheckForPickupPrompt()
    {
        lookedAtTorch = null;

        if (playerCamera == null)
            return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange, itemMask))
        {
            TorchItem torch = hit.collider.GetComponentInParent<TorchItem>();

            if (torch != null && heldTorch == null && !torch.IsHeld)
                lookedAtTorch = torch;
        }

        if (pickupPromptText != null)
        {
            pickupPromptText.gameObject.SetActive(lookedAtTorch != null);

            if (lookedAtTorch != null)
                pickupPromptText.text = "Press E to pick up Torch";
        }
    }

    private void PickupTorch()
    {
        heldTorch = lookedAtTorch;

        if (heldTorch == null)
            return;

        heldTorch.TryPickup(OwnerClientId);
    }

    private void DropTorch()
    {
        if (crankMiniGameUI != null)
            crankMiniGameUI.Hide();

        heldTorch.TryDrop(playerCamera.transform.forward);
        heldTorch = null;
    }

    private void StartCrankMiniGame()
    {
        if (heldTorch == null)
            return;

        if (heldTorch.IsBatteryFull)
            return;

        if (pickupPromptText != null)
            pickupPromptText.gameObject.SetActive(false);

        crankMiniGameUI.Show();
    }

    private void HandleCrankInput()
    {
        bool isHolding = Input.GetKey(KeyCode.R);

        if (wasHoldingCrank && !isHolding)
        {
            CrankResult result = crankMiniGameUI.Submit();
    
            Debug.Log("Crank result submitted: " + result);

            if (heldTorch != null)
                heldTorch.TryCrank(result);

            wasHoldingCrank = false;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            crankMiniGameUI.Hide();
            wasHoldingCrank = false;
        }
    }
}