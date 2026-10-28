using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;
using UnityEngine;

var checks = 0;
void Check(bool condition, string message)
{ if (!condition) throw new Exception("FAIL: " + message); checks++; }

InertBus.Reset();
var vanilla = InertBus.Add(0, true, false);
var lone = InertBus.Add(7);
InertBus.Roster(0, 7);
InertBus.Run(5);
Check(!lone.Pings.Shared && InertBus.History.Count == 0, "Do not probe an ordinary vanilla host's unregistered NGO handler.");
Check(lone.Submit(PingKind.Thanks), "Vanilla-host lobby allows a local preview.");
Check(lone.Shown.Count == 1 && lone.Shown[0].Author == 7, "Local preview belongs to the primary local player.");
Check(vanilla.Shown.Count == 0 && InertBus.History.Count == 0, "Local preview sends no network traffic.");

InertBus.Reset();
var host = InertBus.Add(0, true);
var one = InertBus.Add(7);
var two = InertBus.Add(8);
var unmodded = InertBus.Add(9, false, false);
InertBus.Roster(0, 7, 8, 9);
var nativeBefore = new[] { host, one, two, unmodded }.Select(n => n.GameplayImage()).ToArray();
InertBus.Run(3);
Check(host.Pings.Shared && one.Pings.Shared && two.Pings.Shared, "Two clients negotiate a real production relay with the host.");
Check(InertBus.Unregistered == 0 && InertBus.History.All(e => e.To != 9), "No custom packet sent to vanilla peer.");
Check(host.Submit(PingKind.Thanks), "Host may send an overhead message.");
InertBus.Run(1);
Check(new[] { host, one, two }.All(n => n.Shown.Count == 1 && n.Shown[0].Author == 0 && n.Shown[0].Kind == PingKind.Thanks), "Host message reaches every negotiated participant with author zero.");
Check(one.Submit(PingKind.Location, new(15, 0, 2)), "Client submits nearby point.");
InertBus.Run(1);
Check(new[] { host, one, two }.All(n => n.Shown.Count == 2 && n.Shown[1].Author == 7 && n.Shown[1].PlayerIndex == 1), "Client request is relayed with actual sender and correct roster slot.");
Check(two.Shown[1].Position == new PingPosition(15, 0, 2), "Every peer receives the same location.");
Check(unmodded.Shown.Count == 0 && InertBus.Unregistered == 0, "Vanilla player stays outside the cosmetic channel.");
Check(nativeBefore.SequenceEqual(new[] { host, one, two, unmodded }.Select(n => n.GameplayImage())), "Relay leaves the fixture's roster bindings, positions and spawn flags unchanged.");

var clientRequest = InertBus.History.Last(e => e.From == 7 && e.To == 0 && PingProtocol.Decode(e.Bytes, out var p) && p.Message == PingMessage.Request);
Check(PingProtocol.Decode(clientRequest.Bytes, out var original), "Capture production request bytes for race regressions.");
InertBus.Run(2);
InertBus.Send(7, 0, clientRequest.Channel, clientRequest.Bytes);
InertBus.Run(1);
Check(host.Shown.Count == 2, "Duplicate native callback does not redisplay after cooldown.");

var wrongPlayer = original with { PlayerIndex = 0, Sequence = original.Sequence + 100, Created = InertBus.Now };
InertBus.Send(7, 0, clientRequest.Channel, PingProtocol.Encode(wrongPlayer));
InertBus.Run(1);
Check(host.Shown.Count == 2, "Client cannot speak through the host's character.");
var far = original with { Position = new(10000, 0, 0), Sequence = original.Sequence + 100, Created = InertBus.Now };
InertBus.Send(7, 0, clientRequest.Channel, PingProtocol.Encode(far));
InertBus.Run(1);
Check(host.Shown.Count == 2, "Production host rejects a distant location.");
var badEpoch = original with { Epoch = original.Epoch + 1, Sequence = original.Sequence + 100, Created = InertBus.Now };
InertBus.Send(7, 0, clientRequest.Channel, PingProtocol.Encode(badEpoch));
InertBus.Run(1);
Check(host.Shown.Count == 2, "Production host rejects stale/wrong session epoch.");

var forgedShow = original with { Message = PingMessage.Show, Author = 0, Nonce = 0, Sequence = 1000, Created = InertBus.Now };
InertBus.Send(8, 7, clientRequest.Channel, PingProtocol.Encode(forgedShow));
InertBus.Run(1);
Check(one.Shown.Count == 2, "Client ignores a display packet from another client.");

foreach (var node in new[] { host, one, two }) node.Session.sceneRoot.sceneName = "merchant";
InertBus.Send(7, 0, clientRequest.Channel, PingProtocol.Encode(original with { Sequence = 1001, Created = InertBus.Now }));
InertBus.Run(1);
Check(host.Shown.Count == 2, "Scene-name mismatch rejects an earlier arena request even with reused scene object ID.");
var stale = original with { Sequence = 1002, SceneName = PingProtocol.NameHash("merchant") };
InertBus.Send(7, 0, clientRequest.Channel, PingProtocol.Encode(stale));
InertBus.Run(1);
Check(host.Shown.Count == 2, "An expired request cannot reappear later in the same scene.");
var future = stale with { Sequence = 1003, Created = InertBus.Now + 20 };
InertBus.Send(7, 0, clientRequest.Channel, PingProtocol.Encode(future));
InertBus.Run(1);
Check(host.Shown.Count == 2, "A forged far-future timestamp cannot keep a marker alive.");
var wrongNonce = stale with { Sequence = 1004, Created = InertBus.Now, Nonce = original.Nonce + 1 };
InertBus.Send(7, 0, clientRequest.Channel, PingProtocol.Encode(wrongNonce));
InertBus.Run(1);
Check(host.Shown.Count == 2, "A request must match the sender's negotiated client instance.");
one.Activate();
Check(PingWorld.Local()?.clientID.Value.NetworkID == 7, "Local lookup resolves the account's primary spawned character.");
one.Session.IsPaused = true;
Check(PingWorld.Local() == null && !one.Submit(PingKind.Thanks), "Paused session cannot send a ping.");
one.Session.IsPaused = false;
one.Ex.SceneOperationInProgress = true;
Check(!PingWorld.Ready(out _) && !one.Submit(PingKind.Thanks), "Scene-loading session cannot send a ping.");
one.Ex.SceneOperationInProgress = false;
one.Players.players[1].IsSpawned = false;
Check(PingWorld.Local() == null && !one.Submit(PingKind.Thanks), "Despawned player cannot send a ping.");
one.Players.players[1].IsSpawned = true;
var liveData = one.Players.players[1].playerData;
one.Players.players[1].playerData = new() { Pointer = new(90000), playerID = 1 };
Check(PingWorld.Local() == null, "A stale object with reused slot but different roster binding is rejected.");
one.Players.players[1].playerData = liveData;

// A callback storm is bounded, and malformed buffers do not tear down the channel.
var before = host.Shown.Count;
for (var i = 0; i < 100; i++) InertBus.Send(88, 0, clientRequest.Channel, PingProtocol.Encode(original with { Sequence = (ulong)(2000+i) }));
InertBus.Send(7, 0, clientRequest.Channel, new byte[2048]);
InertBus.Run(1);
Check(host.Pings.Shared && host.Shown.Count == before, "Unknown senders/malformed lengths cannot display or fault the relay.");

host.Activate(); host.Pings.Dispose();
Check(host.Manager.CustomMessagingManager.Handlers.Count == 0, "Dispose unregisters only the ping handler.");
Check(!Steamworks.SteamMatchmaking.Data.ContainsKey("cbmm_ping_host_v2"), "Dispose removes owned ping capability metadata.");
Check(InertBus.DisconnectCalls == 0, "The cosmetic relay never disconnects a native client.");

// Disconnection between maintenance ticks must not disable everyone else's pings.
InertBus.Reset(); host = InertBus.Add(0, true); one = InertBus.Add(7); two = InertBus.Add(8);
InertBus.Roster(0, 7, 8); InertBus.Run(3);
one.Online = false; InertBus.ThrowForOffline = true;
host.Manager.ConnectedClientsIds = new(0, 8);
Check(host.Submit(PingKind.Thanks), "Host still submits when a negotiated peer just disconnected.");
InertBus.Run(1);
Check(host.Pings.Shared && two.Shown.Count == 1, "An offline recipient cannot silence the surviving client.");
// Also cover the gap where the native connected-ID list still contains that peer.
one.Online = true; host.Manager.ConnectedClientsIds = new(0, 7, 8); InertBus.Run(4);
one.Online = false; InertBus.Run(2);
Check(host.Submit(PingKind.Sorry), "Host accepts next ping despite a stale native peer list.");
InertBus.Run(1);
Check(host.Pings.Shared && two.Shown.Count == 2, "Per-peer send failure is contained and healthy peers still receive.");
Check(host.Log.Messages.Any(s => s.Contains("message not delivered")), "The stale-ID-list case actually exercised the send-failure path.");

// A client instance may restart without resetting everybody else's duplicate gates.
one.Online = true; one.ReloadPings(); InertBus.Run(4);
var previous = two.Shown.Count;
Check(one.Pings.Shared && one.Submit(PingKind.Thanks), "Reloaded client re-negotiates with a fresh nonce.");
InertBus.Run(1);
Check(two.Shown.Count == previous + 1 && two.Shown[^1].Author == 7, "Restarted sender sequence does not get stuck behind its old display sequence.");

// The server can re-register its optional relay without changing the native connection.
var beforeReset = host.Shown.Count;
host.ReloadPings(); InertBus.Run(6);
Check(one.Pings.Shared && two.Pings.Shared, "Periodic probes recover a replaced host epoch.");
Check(one.Submit(PingKind.Sorry), "Client can submit after host relay reset.");
InertBus.Run(1);
Check(host.Shown.Count == beforeReset + 1 && host.Shown[^1].Author == 7, "Recovered relay actually delivers instead of merely showing a ready flag.");

// Stop a host's relay while leaving the native game and lobby running.
host.Activate(); host.Pings.Dispose(); InertBus.Run(3);
Check(!one.Pings.Shared && !two.Pings.Shared, "Removed capability falls back to local preview.");
previous = one.Shown.Count;
Check(one.Submit(PingKind.Thanks) && one.Shown.Count == previous + 1, "Local preview remains usable after host relay stops.");

// A delayed server display uses the original lifetime, rather than starting four fresh seconds.
InertBus.Reset(); host = InertBus.Add(0, true); one = InertBus.Add(7); two = InertBus.Add(8);
InertBus.Roster(0, 7, 8); InertBus.Run(3);
Check(host.Submit(PingKind.Thanks), "Capture a host message before artificial network delay.");
InertBus.Now += 2; InertBus.Pump(); one.Tick(); two.Tick();
Check(one.Shown.Count == 1 && one.Lifetimes[0] <= 2 && one.Lifetimes[0] > 0, "Transit time is deducted from an overhead message's remaining life.");
InertBus.Now += 2;
Check(host.Submit(PingKind.Sorry), "Capture a second delayed display.");
InertBus.Now += 5; InertBus.Pump(); one.Tick(); two.Tick();
Check(one.Shown.Count == 1, "A server display older than four seconds is dropped.");

// Delivery jitter can batch legitimate pings that were sent 1.5 seconds apart.
InertBus.Reset(); host = InertBus.Add(0, true); one = InertBus.Add(7);
InertBus.Roster(0, 7); InertBus.Run(3);
Check(host.Submit(PingKind.Thanks), "Capture first host event for batched delivery.");
InertBus.Now += 1.5;
Check(host.Submit(PingKind.Sorry), "Capture second host event at the allowed source interval.");
InertBus.Pump(); one.Tick();
Check(one.Shown.Count == 2 && one.Shown[^1].Kind == PingKind.Sorry, "Compressed arrival times do not discard the newest legitimate display.");

InertBus.Reset(); host = InertBus.Add(0, true); one = InertBus.Add(7);
InertBus.Roster(0, 7); InertBus.Run(3);
Check(one.Submit(PingKind.Thanks), "Capture first client request for batched delivery.");
InertBus.Now += 1.5;
Check(one.Submit(PingKind.Sorry), "Capture second client request at the allowed source interval.");
InertBus.Pump(); host.Tick();
Check(host.Shown.Count == 2 && host.Shown[^1].Kind == PingKind.Sorry, "Host cooldown uses fresh validated source times despite batched request arrival.");

// Missing acknowledgements expire capability, and a resumed relay can negotiate again.
InertBus.Reset(); host = InertBus.Add(0, true); one = InertBus.Add(7);
InertBus.Roster(0, 7); InertBus.Run(3); host.PingTicksEnabled = false;
InertBus.Run(15);
Check(!one.Pings.Shared, "Silent relay stops being advertised as shared after the acknowledgement lease expires.");
previous = one.Shown.Count;
Check(one.Submit(PingKind.Thanks) && one.Shown.Count == previous + 1, "Expired relay lease still allows a clearly local preview.");
host.PingTicksEnabled = true; InertBus.Run(6);
Check(one.Pings.Shared, "A resumed relay recovers through fresh correlated acknowledgements.");
previous = host.Shown.Count;
Check(one.Submit(PingKind.Sorry), "Recovered lease permits a real outgoing request.");
InertBus.Run(1);
Check(host.Shown.Count == previous + 1, "Lease recovery establishes delivery, not only a ready flag.");

Check(InertBus.DisconnectCalls == 0, "Disconnect remains game-owned in all recovery/delay cases.");

ControllerChecks.Run(Check);
GamepadChecks.Run(Check);
HostToolsChecks.Run(Check);
PartyChatChecks.Run(Check);
SoloChatChecks.Run(Check);
ChatFilterChecks.Run(Check);
if (args.Length > 0) ArtifactChecks.Run(args[0], Check);
Console.WriteLine($"PASS: {checks} production-adapter/controller integration checks with inert NGO/Steam/Unity substitutes. No game/runtime DLLs loaded.");
