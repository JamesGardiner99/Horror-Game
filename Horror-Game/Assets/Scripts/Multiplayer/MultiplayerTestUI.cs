using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayTestUI : MonoBehaviour
{
    private string joinCode = "";
    private string joinCodeInput = "";
    private string status = "Not connected";

    private async void Start()
    {
        await InitialiseUnityServices();
    }

    private async Task InitialiseUnityServices()
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            status = "Signed in";
        }
        catch (Exception e)
        {
            status = "Unity Services failed";
            Debug.LogException(e);
        }
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 350, 250));

        GUILayout.Label($"Status: {status}");

        if (!string.IsNullOrWhiteSpace(joinCode))
            GUILayout.Label($"Join Code: {joinCode}");

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Host Online Game"))
                _ = StartHostWithRelay();

            GUILayout.Space(10);

            GUILayout.Label("Join Code:");
            joinCodeInput = GUILayout.TextField(joinCodeInput).ToUpper();

            if (GUILayout.Button("Join Online Game"))
                _ = StartClientWithRelay(joinCodeInput);
        }
        else
        {
            if (NetworkManager.Singleton.IsHost)
                GUILayout.Label("Running as Host");

            if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
                GUILayout.Label("Running as Client");
        }

        GUILayout.EndArea();
    }

    private async Task StartHostWithRelay()
    {
        try
        {
            status = "Creating Relay allocation...";

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3);

            joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            UnityTransport transport =
                NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetRelayServerData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            NetworkManager.Singleton.StartHost();

            status = "Host started";
            Debug.Log($"Relay Join Code: {joinCode}");
        }
        catch (Exception e)
        {
            status = "Failed to host";
            Debug.LogException(e);
        }
    }

    private async Task StartClientWithRelay(string code)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                status = "Enter a join code first";
                return;
            }

            status = "Joining Relay...";

            JoinAllocation joinAllocation =
                await RelayService.Instance.JoinAllocationAsync(code);

            UnityTransport transport =
                NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetRelayServerData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            NetworkManager.Singleton.StartClient();

            status = "Client started";
        }
        catch (Exception e)
        {
            status = "Failed to join";
            Debug.LogException(e);
        }
    }
}