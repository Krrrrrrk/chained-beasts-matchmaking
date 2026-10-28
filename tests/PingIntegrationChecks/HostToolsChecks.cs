using System.Reflection;
using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

static class HostToolsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        InertBus.Reset(); Plugin.HostToolsEnabled = true;
        var host = InertBus.Add(0, true, false); var guest = InertBus.Add(7, false, false); InertBus.Roster(0, 7);
        host.Activate(); var service = new HostModerationService(); Plugin.HostTools = service;
        var transport = (SteamTransportController)host.Ex.Transport;
        service.Tick();
        check(service.Policy.Active && service.Policy.Peers().Single().Account == 10007, "Host reads the stable account from the Steam transport mapping.");
        transport.clientPayloads[7].playerID = "10000"; transport.clientPayloads[7].displayName = "I am the host";
        check(service.Policy.Peers().Single().Account == 10007, "Spoofed connection-payload playerID/name cannot change socket identity.");
        check(service.Policy.RequestKick(7, 10007) && service.Policy.ConfirmKick(), "Host confirmation calls the real moderation adapter's disconnect path.");
        check(InertBus.Disconnects.Single().Host == 0 && InertBus.Disconnects[0].Client == 7, "Exactly the selected remote NGO connection is disconnected.");
        var request = new NetworkManager.ConnectionApprovalRequest { ClientNetworkId = 7 };
        var response = new NetworkManager.ConnectionApprovalResponse();
        check(!service.BeforeApproval(transport, request, response) && !response.Approved && !response.Pending && !response.CreatePlayerObject,
            "Banned native approval skips original and explicitly rejects all approval flags.");
        check(response.Reason.Contains("blocked"), "Banned connection receives a reason.");
        host.Roster(0, 19); // Simulate a fresh NGO ID on reconnect, with the same authenticated Steam socket account.
        transport = (SteamTransportController)host.Ex.Transport;
        transport.transport.connectionMapping[10019] = new() { id = new(10007) };
        request.ClientNetworkId = 19; response = new();
        check(!service.BeforeApproval(transport, request, response), "Reconnecting under a fresh NGO ID cannot bypass the native admission hook.");
        var count = InertBus.Disconnects.Count; service.Tick(); service.Tick();
        check(InertBus.Disconnects.Count == count + 1 && InertBus.Disconnects[^1].Client == 19,
            "An already-admitted racing connection is disconnected once on the next host update.");
        host.Manager.ConnectionManager.Map.Remove(19); response = new();
        check(!service.BeforeApproval(transport, request, response) && !response.Approved, "Unverifiable incoming identity fails closed only while lobby blocks exist.");
        check(service.Policy.Unblock(10007), "The host can remove a block through the production service policy.");
        var lookups = host.Manager.ConnectionManager.Lookups;
        response = new() { Approved = false, Pending = true, CreatePlayerObject = true, Reason = "original" };
        check(service.BeforeApproval(transport, request, response) && !response.Approved && response.Pending && response.CreatePlayerObject && response.Reason == "original",
            "Unblocked/unknown identity leaves original native response entirely unchanged.");
        check(host.Manager.ConnectionManager.Lookups == lookups, "With no blocks the hook does not inspect or interfere with a native incoming identity lookup.");
        host.Roster(0, 7); service.Policy.RequestKick(7, 10007); service.Policy.ConfirmKick();
        guest.Activate(); response = new(); request.ClientNetworkId = 0;
        check(service.BeforeApproval((SteamTransportController)guest.Ex.Transport, request, response) && response.Approved,
            "The same hook never rejects requests on a joining client's PC.");
        check(!service.Policy.RequestKick(0, 10000) && !service.Policy.ConfirmKick(), "Client-side UI/backend cannot exercise host authority.");
        host.Activate(); service.Tick();
        check(service.Policy.Blocked.Count == 0, "Ending host authority clears the old local scope.");
        service.Policy.RequestKick(7, 10007); service.Policy.ConfirmKick();
        Steamworks.SteamUser.Self = default; service.Tick();
        check(service.Policy.Blocked.Count == 1, "A transient Steam self-ID lookup failure does not erase the live host's blocks.");
        host.Activate(); transport = (SteamTransportController)host.Ex.Transport;
        var expectedScope = service.ReadScope(); var beforeMismatch = InertBus.Disconnects.Count;
        transport.transport.connectionMapping[10007] = new() { id = new(10008) };
        var refusedMismatch = false;
        try { service.Disconnect(expectedScope, 7, 10007, HostModeration.KickReason); } catch (InvalidOperationException) { refusedMismatch = true; }
        check(refusedMismatch && InertBus.Disconnects.Count == beforeMismatch, "Native disconnect adapter rechecks the authenticated target immediately before acting.");
        transport.transport.connectionMapping[10007] = new() { id = new(10007) };
        refusedMismatch = false;
        try { service.Disconnect(expectedScope with { Lobby = 999 }, 7, 10007, HostModeration.KickReason); } catch (InvalidOperationException) { refusedMismatch = true; }
        check(refusedMismatch && InertBus.Disconnects.Count == beforeMismatch, "Native disconnect adapter refuses a stale lobby scope.");
        var oldTransport = new SteamTransportController { lobbyID = transport.lobbyID };
        request.ClientNetworkId = 7; response = new();
        check(service.BeforeApproval(oldTransport, request, response), "A stale transport instance cannot apply current-lobby policy to a different callback.");
        request.ClientNetworkId = 0; response = new();
        check(service.BeforeApproval(transport, request, response), "Native host's own approval remains untouched.");
        host.Ex.isLeavingLobby = true; service.Tick();
        check(!service.Policy.Active && service.Policy.Blocked.Count == 0, "Native lobby leave clears the session ledger.");
        host.Ex.isLeavingLobby = false; service.Tick(); service.Policy.RequestKick(7, 10007); service.Policy.ConfirmKick();
        transport.lobbyID = new(0); service.Tick();
        check(!service.Policy.Active && service.Policy.Blocked.Count == 0, "Loss of the actual hosted lobby ends its block scope.");

        // Drive the production host controller with inert key states and view action queues.
        InertBus.Reset(); host = InertBus.Add(0, true, false); guest = InertBus.Add(7, false, false); InertBus.Roster(0, 7);
        Plugin.HostToolsEnabled = true; host.Activate(); Plugin.HostTools = service = new();
        Keyboard.current = new(); Application.isFocused = true; Cursor.visible = false; Cursor.lockState = CursorLockMode.Locked;
        var controller = new HostToolsController(new(90000));
        var view = (HostToolsView)typeof(HostToolsController).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        void Frame(Node node, bool held)
        {
            node.Activate(); InertBus.Now += .1; Time.realtimeSinceStartupAsDouble = InertBus.Now;
            Keyboard.current[Key.F8].Set(held); Keyboard.current.escapeKey.Set(false); controller.Update(); controller.OnGUI();
        }
        Frame(host, true);
        check(HostToolsController.IsOpen && Cursor.visible && Cursor.lockState == CursorLockMode.None, "F8 opens host controls during a live run.");
        Frame(host, false); view.KickClient = 7; view.KickAccount = 10007; view.KickRequested = true; Frame(host, false);
        check(service.Policy.Pending?.Account == 10007 && InertBus.Disconnects.Count == 0, "Queued Kick button only creates a confirmation.");
        view.ConfirmRequested = true; Frame(host, false); Frame(host, false);
        check(InertBus.Disconnects.Count == 1 && service.Policy.Blocked.Count == 1, "Queued confirmation performs one native disconnect and retains the block.");
        view.UnblockAccount = 10007; view.UnblockRequested = true; Frame(host, false);
        check(service.Policy.Blocked.Count == 0, "Allow rejoin UI action removes the account block.");
        Frame(host, true);
        check(!HostToolsController.IsOpen && !Cursor.visible && Cursor.lockState == CursorLockMode.Locked, "F8 close restores gameplay cursor state.");
        Frame(host, false); Frame(guest, true);
        check(!HostToolsController.IsOpen, "A remote client cannot open host controls with F8.");
        controller.OnDestroy(); Plugin.HostTools = null;
        check(InertBus.History.Count == 0, "Host moderation works without exchanging custom mod packets with the kicked player.");
    }
}
