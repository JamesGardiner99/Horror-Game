using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class TorchItem : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Light torchLight;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider itemCollider;

    [Header("Torch")]
    [SerializeField] private float maxBattery = 100f;
    [SerializeField] private float drainPerSecond = 3f;

    [Header("Light Settings")]
    [SerializeField] private float maxIntensity = 3f;
    [SerializeField] private float minIntensity = 0.5f;
    [SerializeField] private float dimStartThreshold = 0.5f;
    [SerializeField] private float lightRange = 10f;

    [Header("Drop")]
    [SerializeField] private float dropForwardForce = 2f;
    [SerializeField] private float dropUpForce = 1f;


    [SerializeField] private float missRechargeAmount = 5f;
    [SerializeField] private float goodRechargeAmount = 25f;
    [SerializeField] private float perfectRechargeAmount = 50f;

    private NetworkVariable<bool> isHeld = new(false);
    private NetworkVariable<bool> isOn = new(false);
    private NetworkVariable<float> battery = new(100f);

    public bool IsBatteryFull => battery.Value >= maxBattery;

    private NetworkVariable<ulong> heldByClientId = new(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsHeld => isHeld.Value;

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (itemCollider == null)
            itemCollider = GetComponent<Collider>();
    }

    private void Update()
    {
        if (IsServer)
            DrainBattery();

        FollowHoldPoint();
        ApplyVisuals();
    }

    public void TryPickup(ulong playerId)
    {
        PickupServerRpc(playerId);
    }

    public void TryDrop(Vector3 forward)
    {
        DropServerRpc(forward);
    }

    public void TryToggle()
    {
        ToggleServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void PickupServerRpc(ulong playerId)
    {
        if (isHeld.Value)
            return;

        isHeld.Value = true;
        heldByClientId.Value = playerId;

        NetworkObject.ChangeOwnership(playerId);

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (itemCollider != null)
            itemCollider.enabled = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void DropServerRpc(Vector3 forward)
    {
        if (!isHeld.Value)
            return;

        isHeld.Value = false;
        heldByClientId.Value = ulong.MaxValue;

        transform.SetParent(null);

        if (itemCollider != null)
            itemCollider.enabled = true;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            Vector3 dropVelocity = forward.normalized * dropForwardForce + Vector3.up * dropUpForce;
            rb.AddForce(dropVelocity, ForceMode.VelocityChange);
        }

        NetworkObject.RemoveOwnership();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleServerRpc()
    {
        if (battery.Value <= 0f)
            return;

        isOn.Value = !isOn.Value;
    }

    private void FollowHoldPoint()
    {
        if (!isHeld.Value || heldByClientId.Value == ulong.MaxValue)
            return;

        NetworkObject playerObj =
            NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(heldByClientId.Value);

        if (playerObj == null)
            return;

        PlayerItemController itemController =
            playerObj.GetComponent<PlayerItemController>();

        if (itemController == null)
            return;

        Transform holdPoint = itemController.GetHoldPoint();

        if (holdPoint == null)
            return;

        transform.position = holdPoint.position;
        transform.rotation = holdPoint.rotation;
    }

    private void DrainBattery()
    {
        if (!isOn.Value || battery.Value <= 0f)
            return;

        battery.Value -= drainPerSecond * Time.deltaTime;

        if (battery.Value <= 0f)
        {
            battery.Value = 0f;
            isOn.Value = false;
        }
    }

    public void TryCrank(CrankResult result)
    {
        CrankServerRpc(result);
    }

    [ServerRpc(RequireOwnership = false)]
    private void CrankServerRpc(CrankResult result)
    {
        float rechargeAmount = result switch
        {
            CrankResult.Perfect => perfectRechargeAmount,
            CrankResult.Good => goodRechargeAmount,
            _ => missRechargeAmount
        };

        float before = battery.Value;

        battery.Value += rechargeAmount;
        battery.Value = Mathf.Clamp(battery.Value, 0f, maxBattery);

        Debug.Log($"Crank result: {result}, added: {rechargeAmount}, before: {before}, after: {battery.Value}");
    }

    private void ApplyVisuals()
    {
        if (torchLight == null)
            return;

        bool lightOn = isOn.Value && battery.Value > 0f;
        torchLight.enabled = lightOn;

        if (!lightOn)
            return;

        float batteryPercent = battery.Value / maxBattery;

        float intensity = batteryPercent > dimStartThreshold
            ? maxIntensity
            : Mathf.Lerp(minIntensity, maxIntensity, batteryPercent / dimStartThreshold);

        if (batteryPercent <= 0.2f)
        {
            float flicker = Mathf.PerlinNoise(Time.time * 20f, 0f);
            intensity *= Mathf.Lerp(0.5f, 1f, flicker);
        }

        torchLight.intensity = intensity;
        torchLight.range = lightRange;
    }
}