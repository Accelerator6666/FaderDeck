using FaderDeck.Core;
using FaderDeck.Obs;
using FaderDeck.Reaper;

int count = 0;
void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    count++;
}

Assert(DeviceProtocol.Move(0, 128, 255) == "MOVE 0 128 255", "motor frame");
Assert(DeviceProtocol.Layer(7) == "LAYER 7", "layer frame");
Assert(DeviceProtocol.TryParseState("STATE 2 0 128 1", out var state), "state parse");
Assert(state.Mode == 2 && state.Layer == 0 && state.Position == 128 && state.Touch, "state values");
Assert(!DeviceProtocol.TryParseState("STATE 2 8 128 1", out _), "invalid layer rejection");
Assert(!DeviceProtocol.TryParseState("STATE 2 0 256 1", out _), "out of range rejection");
Assert(!DeviceProtocol.TryParseState("STATE 2 0 128 2", out _), "invalid touch rejection");
Assert(!DeviceProtocol.TryParseState("STATE 9 0 128 0", out _), "invalid mode rejection");
Assert(DeviceProtocol.TryParseInfo("INFO 5 1 5 0123456789ABCDEFabcd", out var info), "info parse");
Assert(info.Protocol == 5 && info.FirmwareMajor == 1 && info.FirmwareMinor == 5, "version parse");
Assert(info.Serial == "0123456789ABCDEFABCD", "serial normalized");
Assert(!DeviceProtocol.TryParseInfo("INFO 5 1 5 Z123456789ABCDEFABCD", out _), "invalid serial char rejected");
Assert(!DeviceProtocol.TryParseInfo("INFO 5 1 5 012345", out _), "invalid serial length rejected");
Assert(!DeviceProtocol.TryParseInfo("INFO 999 1 5 0123456789ABCDEFABCD", out _), "invalid version range rejected");

var gate = new SyncGate();
Assert(!gate.TryGetMotorTarget("track.1.volume", false, out _), "unknown value must not actuate");
gate.Observe("track.1.volume", 0.5, ValueConfidence.Cached);
Assert(!gate.TryGetMotorTarget("track.1.volume", false, out _), "cached value must not actuate");
gate.Observe("track.1.volume", 0.5, ValueConfidence.Verified);
Assert(gate.TryGetMotorTarget("track.1.volume", false, out byte value) && value == 128, "verified midpoint");
Assert(!gate.TryGetMotorTarget("track.1.volume", true, out _), "touch blocks motor");
gate.Disconnect();
Assert(!gate.TryGetMotorTarget("track.1.volume", false, out _), "disconnect invalidates feedback");

var sim = new VirtualFader();
Assert(sim.Execute("PING") == "PONG 1", "sim ping");
Assert(DeviceProtocol.TryParseInfo(sim.Execute("INFO"), out var virtualInfo), "sim info parse");
Assert(virtualInfo.Protocol == 5 && virtualInfo.FirmwareMajor == 1, "sim firmware identity");
Assert(DeviceProtocol.TryParseState(sim.Execute("STATE"), out var initial), "sim state parse");
Assert(initial.Mode == 2 && initial.Layer == 0 && initial.Position == 128 && !initial.Touch, "sim idle startup");
Assert(sim.Execute("MOVE 0 210 128") == "OK MOVE", "sim active move");
Assert(sim.State.Position == 210 && sim.GetLayerTarget(0) == 210, "sim target updates");
Assert(sim.Execute("MOVE 1 35 128") == "OK MOVE", "sim inactive move");
Assert(sim.State.Position == 210 && sim.GetLayerTarget(1) == 35, "inactive layer cannot drive current layer");
Assert(sim.Execute("LAYER 1") == "OK LAYER", "sim layer switch");
Assert(sim.State.Position == 35 && sim.State.Layer == 1, "sim layer position restoration");
sim.SetTouch(true);
Assert(sim.Execute("MOVE 1 98 128") == "ERR FADER_BUSY_OR_TOUCHED", "touch refuses move");
Assert(sim.Execute("LAYER 0") == "ERR FADER_BUSY_OR_TOUCHED", "touch refuses layer");
sim.ManualMove(40);
Assert(sim.State.Position == 40 && sim.GetLayerTarget(1) == 40, "manual touch move updates layer");
Assert(sim.Execute("STATE") == "STATE 1 1 40 1", "touch state frame");
sim.SetTouch(false);
Assert(sim.Execute("STATE") == "STATE 2 1 40 0", "touch release state frame");
sim.SetBusy(true);
Assert(sim.Execute("MOVE 1 60 0") == "ERR FADER_BUSY_OR_TOUCHED", "moving state blocks second move");
sim.SetBusy(false);
sim.SetFault(true);
Assert(sim.Execute("CALIBRATE") == "ERR FADER_BUSY_OR_TOUCHED", "fault blocks calibration");
sim.SetFault(false);
Assert(sim.Execute("CALIBRATE") == "OK CALIBRATE", "sim calibration command");
Assert(sim.State.Mode == 4, "calibration mode reported");
Assert(sim.Execute("MOVE 1 77 128") == "ERR FADER_BUSY_OR_TOUCHED", "calibration blocks move");
sim.FinishCalibration();
Assert(sim.State.Mode == 2, "calibration completes to idle");
sim.SetConnected(false);
Assert(sim.Execute("INFO") == "ERR I2C_READ", "disconnect reports unavailable");
Assert(sim.Execute("PING") == "PONG 1", "usb still pings without I2C device");
Assert(sim.Execute("MOVE 1 20 128") == "ERR I2C_READ", "disconnect prevents move");
sim.SetConnected(true);
sim.SetProtocol(4);
Assert(sim.Execute("STATE") == "ERR UNSUPPORTED_PROTOCOL", "reject unsupported protocol state");
Assert(sim.Execute("MOVE 1 20 128") == "ERR UNSUPPORTED_PROTOCOL", "reject unsupported protocol move");
sim.SetProtocol(5);
Assert(sim.Execute("MOVE 1 20 128") == "OK MOVE", "recover from protocol mismatch");
Assert(sim.Execute("MOVE 8 0 128") == "ERR INVALID_MOVE", "reject invalid layer");
Assert(sim.Execute("MOVE 1 256 128") == "ERR INVALID_MOVE", "reject out-of-range position");
Assert(sim.Execute("MOVE 1 200 999") == "ERR INVALID_MOVE", "reject out-of-range speed");
Assert(sim.Execute("MOVE 1 20 128 trailing") == "ERR INVALID_MOVE", "reject extra args");
Assert(sim.Execute("LAYER 8") == "ERR INVALID_LAYER", "reject invalid layer change");
Assert(sim.Execute("CLEAR_ERROR") == "ERR UNKNOWN_COMMAND", "sim only supports M0 commands");

Assert(ObsProtocol.IsAllowedEndpoint(new Uri("ws://127.0.0.1:4455")), "local OBS WS allowed");
Assert(ObsProtocol.IsAllowedEndpoint(new Uri("ws://localhost:4455")), "localhost OBS WS allowed");
Assert(ObsProtocol.IsAllowedEndpoint(new Uri("wss://example.org:4455")), "remote TLS OBS allowed");
Assert(!ObsProtocol.IsAllowedEndpoint(new Uri("ws://example.org:4455")), "remote plaintext OBS denied");
Assert(ObsProtocol.SliderToMul(0) == 0, "0% OBS volume is silent");
Assert(Math.Abs(ObsProtocol.SliderToMul(100) - 1.0) < 1e-12, "100% OBS volume = unity");
Assert(Math.Abs(ObsProtocol.DbToSlider(-60)) < 1e-9, "minimum slider at -60dB");
Assert(Math.Abs(ObsProtocol.DbToSlider(0) - 100) < 1e-9, "maximum slider at 0dB");
Assert(Math.Abs(ObsProtocol.DbToSlider(ObsProtocol.SliderToDb(35)) - 35) < 1e-9, "OBS slider roundtrip");
Assert(ObsProtocol.DbToSlider(double.NegativeInfinity) == 0, "negative infinity maps to zero");
string obsAuth = ObsProtocol.Authentication("secret", "salt", "challenge");
Assert(obsAuth.Length == 44 && obsAuth.EndsWith('='), "OBS auth is base64 SHA256");
Assert(obsAuth == ObsProtocol.Authentication("secret", "salt", "challenge"), "OBS auth deterministic");
Assert(obsAuth != ObsProtocol.Authentication("bad", "salt", "challenge"), "OBS auth depends on password");

var mapping = new ObsMappingProfile();
string[] obsInputs = ["Microphone", "Desktop Audio", "Music", "Game", "Narration"];
Assert(mapping.Get(0, 0) == FaderBinding.Auto, "default mapping uses Auto");
Assert(mapping.Resolve(0, 0, obsInputs) == "Microphone", "auto bank 0 slot 0");
Assert(mapping.Resolve(0, 3, obsInputs) == "Game", "auto fourth slot");
Assert(mapping.Resolve(1, 0, obsInputs) == "Narration", "second bank first slot");
Assert(mapping.Resolve(1, 1, obsInputs) is null, "auto slot beyond available sources disabled");
mapping.Set(0, 0, FaderBinding.ForInput("Music"));
Assert(mapping.Resolve(0, 0, obsInputs) == "Music", "explicit binding overrides order");
Assert(mapping.Resolve(0, 0, ["Desktop Audio", "Music"]) == "Music", "explicit remains stable after reorder");
Assert(mapping.Resolve(0, 0, ["Microphone"]) is null, "missing explicit input never falls back");
mapping.Set(0, 1, FaderBinding.Unassigned);
Assert(mapping.Resolve(0, 1, obsInputs) is null, "unassigned slot disabled");
Assert(mapping.Get(0, 2) == FaderBinding.Auto, "other slot unchanged");
Assert(mapping.Get(1, 0) == FaderBinding.Auto, "other bank unchanged");

string serialized = mapping.ToJson();
Assert(!serialized.Contains("password", StringComparison.OrdinalIgnoreCase), "profile cannot contain OBS password");
var fromJson = ObsMappingProfile.FromJson(serialized);
Assert(fromJson.Get(0, 0) == FaderBinding.ForInput("Music"), "explicit JSON round trip");
Assert(fromJson.Get(0, 1) == FaderBinding.Unassigned, "unassigned JSON round trip");
Assert(fromJson.Get(1, 0) == FaderBinding.Auto, "default JSON round trip");

string workingDir = Path.Combine(Path.GetTempPath(), "faderdeck-map-test-" + Guid.NewGuid().ToString("N"));
string path = Path.Combine(workingDir, "obs-mappings.json");
try
{
    mapping.Save(path);
    Assert(File.Exists(path), "profile saved");
    Assert(ObsMappingProfile.Load(path).Resolve(0, 0, obsInputs) == "Music", "profile reload");
    mapping.Set(0, 0, FaderBinding.ForInput("Microphone"));
    mapping.Save(path);
    Assert(ObsMappingProfile.Load(path).Resolve(0, 0, obsInputs) == "Microphone", "atomic replacement");
}
finally
{
    if (Directory.Exists(workingDir)) Directory.Delete(workingDir, true);
}

mapping.ResetBank(0);
Assert(mapping.Get(0, 0) == FaderBinding.Auto && mapping.Get(0, 1) == FaderBinding.Auto, "bank reset");
Assert(mapping.Get(1, 0) == FaderBinding.Auto, "bank reset does not alter other banks");

void MustReject(string json, string scenario)
{
    bool rejected = false;
    try { _ = ObsMappingProfile.FromJson(json); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, scenario);
}
MustReject("{\"version\":99,\"mappings\":[]}", "unknown profile format rejected");
MustReject("{\"version\":1,\"mappings\":null}", "null mappings rejected");
MustReject("{\"version\":1,\"mappings\":[{\"bank\":8,\"slot\":0,\"mode\":\"Auto\"}]}", "invalid bank rejected");
MustReject("{\"version\":1,\"mappings\":[{\"bank\":0,\"slot\":4,\"mode\":\"Auto\"}]}", "invalid slot rejected");
MustReject("{\"version\":1,\"mappings\":[{\"bank\":0,\"slot\":0,\"mode\":\"Unexpected\"}]}", "invalid mode rejected");
MustReject("{\"version\":1,\"mappings\":[{\"bank\":0,\"slot\":0,\"mode\":\"Explicit\"}]}", "explicit missing source rejected");
MustReject("{\"version\":1,\"mappings\":[{\"bank\":0,\"slot\":0,\"mode\":\"Auto\"},{\"bank\":0,\"slot\":0,\"mode\":\"Unassigned\"}]}", "duplicate mapping rejected");
MustReject("{not json}", "malformed profile rejected");

var universal = new ControlProfiles();
Assert(universal.Resolve(ControlAdapters.Obs, 0, 0, ["Mic", "Music"]) ==
    new ResolvedControl("obs", "input.volume", "Mic"), "generic OBS auto binding");
Assert(universal.Resolve(ControlAdapters.Obs, 0, 1, ["Mic", "Music"]) ==
    new ResolvedControl("obs", "input.volume", "Music"), "generic OBS second slot");
Assert(universal.Resolve(ControlAdapters.Obs, 0, 2, ["Mic", "Music"]) is null, "generic missing OBS auto binding");
Assert(universal.Resolve(ControlAdapters.Reaper, 0, 0) ==
    new ResolvedControl("reaper", "track.volume", "1"), "REAPER first track auto");
Assert(universal.Resolve(ControlAdapters.Reaper, 7, 3) ==
    new ResolvedControl("reaper", "track.volume", "32"), "REAPER last track auto");
Assert(universal.Resolve(ControlAdapters.Davinci, 0, 0) is null, "DaVinci unsupported native parameter cannot be auto bound");
Assert(!ControlAdapters.Get("davinci", "primaries.contrast").CanWrite, "DaVinci contrast explicitly unsupported");
Assert(ControlAdapters.Get("reaper", "track.volume").HasFeedback, "REAPER volume supports feedback");
Assert(!ControlAdapters.Get("reaper", "track.volume").CanRead, "REAPER has no synchronous OSC snapshot API");

universal.Set("obs", 0, 0, ControlBinding.Bind("input.volume", "Music"));
Assert(universal.Resolve("obs", 0, 0, ["Music", "Mic"])?.Target == "Music", "generic OBS explicit stable when reordered");
Assert(universal.Resolve("obs", 0, 0, ["Mic"]) is null, "generic OBS missing explicit fails closed");
universal.Set("reaper", 0, 0, ControlBinding.Bind("track.volume", "7"));
Assert(universal.Resolve("reaper", 0, 0)?.Target == "7", "generic REAPER explicit target");
universal.Set("reaper", 0, 1, ControlBinding.Unassigned);
Assert(universal.Resolve("reaper", 0, 1) is null, "generic disabled REAPER slot");
Assert(universal.Get("reaper", 1, 0) == ControlBinding.Auto, "generic banks isolated");
Assert(universal.Get("obs", 0, 0).Target == "Music", "generic adapters isolated");
Assert(!universal.ToJson().Contains("password", StringComparison.OrdinalIgnoreCase), "universal profiles do not store OBS credentials");
var reconstructed = ControlProfiles.FromJson(universal.ToJson());
Assert(reconstructed.Get("reaper", 0, 0) == ControlBinding.Bind("track.volume", "7"), "universal profile JSON roundtrip REAPER");
Assert(reconstructed.Get("obs", 0, 0) == ControlBinding.Bind("input.volume", "Music"), "universal profile JSON roundtrip OBS");
universal.ResetBank("reaper", 0);
Assert(universal.Get("reaper", 0, 0) == ControlBinding.Auto, "generic bank reset");
Assert(universal.Get("obs", 0, 0).Target == "Music", "generic reset does not affect other adapters");

bool RejectBinding(Action test)
{
    try { test(); return false; }
    catch (ArgumentException) { return true; }
}
Assert(RejectBinding(() => universal.Set("reaper", 0, 0,
    ControlBinding.Bind("track.volume", "33"))), "REAPER beyond range rejected");
Assert(RejectBinding(() => universal.Set("reaper", 0, 0,
    ControlBinding.Bind("track.volume", "01"))), "REAPER noncanonical target rejected");
Assert(RejectBinding(() => universal.Set("davinci", 0, 0,
    ControlBinding.Bind("primaries.contrast", "contrast"))), "unsupported DaVinci actuator blocked");
Assert(RejectBinding(() => universal.Set("obs", 0, 0,
    ControlBinding.Bind("input.volume", ""))), "empty target rejected");

var legacy = new ObsMappingProfile();
legacy.Set(2, 1, FaderBinding.ForInput("Mic"));
legacy.Set(3, 0, FaderBinding.Unassigned);
var migrated = ControlProfiles.ImportLegacyObs(legacy);
Assert(migrated.Get("obs", 2, 1) == ControlBinding.Bind("input.volume", "Mic"), "legacy OBS explicit imported");
Assert(migrated.Get("obs", 3, 0) == ControlBinding.Unassigned, "legacy OBS unassigned imported");
Assert(migrated.Get("reaper", 2, 1) == ControlBinding.Auto, "import does not change REAPER");
Assert(migrated.Resolve("obs", 2, 1, ["Mic"])?.Target == "Mic", "migrated mapping resolves");

string universalFolder = Path.Combine(Path.GetTempPath(), "faderdeck-profiles-" + Guid.NewGuid().ToString("N"));
try
{
    string legacyFile = Path.Combine(universalFolder, "obs.json");
    string profileFile = Path.Combine(universalFolder, "profiles.json");
    Directory.CreateDirectory(universalFolder);
    legacy.Save(legacyFile);
    var imported = ControlProfiles.LoadOrImport(profileFile, legacyFile);
    Assert(imported.Get("obs", 2, 1).Target == "Mic", "load/import old profile");
    imported.Save(profileFile);
    Assert(File.Exists(legacyFile), "migration leaves old file intact");
    Assert(ControlProfiles.LoadOrImport(profileFile, legacyFile).Get("obs", 2, 1).Target == "Mic", "new profile persisted");
}
finally
{
    if (Directory.Exists(universalFolder)) Directory.Delete(universalFolder, true);
}

void MustRejectUniversal(string json, string reason)
{
    bool rejected = false;
    try { _ = ControlProfiles.FromJson(json); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, reason);
}
MustRejectUniversal("{\"version\":2,\"bindings\":[]}", "reject future profile schema");
MustRejectUniversal("{\"version\":1,\"bindings\":[{\"app\":\"reaper\",\"bank\":0,\"slot\":0,\"mode\":\"Explicit\",\"parameterId\":\"track.volume\",\"target\":\"33\"}]}", "invalid REAPER target JSON rejected");
MustRejectUniversal("{\"version\":1,\"bindings\":[{\"app\":\"reaper\",\"bank\":0,\"slot\":0,\"mode\":\"Unassigned\"},{\"app\":\"reaper\",\"bank\":0,\"slot\":0,\"mode\":\"Unassigned\"}]}", "generic duplicate rejected");
MustRejectUniversal("{\"version\":1,\"bindings\":[{\"app\":\"davinci\",\"bank\":0,\"slot\":0,\"mode\":\"Explicit\",\"parameterId\":\"primaries.contrast\",\"target\":\"contrast\"}]}", "unsupported DaVinci JSON blocked");
MustRejectUniversal("{INVALID", "malformed generic JSON blocked");

byte[] trackPacket = OscCodec.EncodeTrackVolume(3, 0.25);
Assert(OscCodec.TryDecodeTrackVolume(trackPacket, out int packetTrack, out double packetValue), "OSC track volume roundtrip");
Assert(packetTrack == 3 && Math.Abs(packetValue - 0.25) < 1e-6, "OSC 32-bit float and track");
Assert(OscCodec.TryDecodeFloat(trackPacket, out var oscAddress, out float oscFloat)
    && oscAddress == "/track/3/volume" && Math.Abs(oscFloat - .25f) < 1e-6f, "OSC address and float");
Assert(!OscCodec.TryDecodeTrackVolume(trackPacket.AsSpan(0, trackPacket.Length - 1), out _, out _), "truncated OSC packet rejected");
Assert(!OscCodec.TryDecodeTrackVolume(OscCodec.EncodeFloat("/track/1/pan", .5f), out _, out _), "OSC unrelated parameter rejected");
Assert(!OscCodec.TryDecodeTrackVolume(OscCodec.EncodeFloat("/track/1/volume", 1.5f), out _, out _), "OSC out-of-range volume rejected");
Assert(RejectBinding(() => { _ = OscCodec.EncodeTrackVolume(0, 0.25); }), "OSC track zero blocked");
Assert(RejectBinding(() => { _ = OscCodec.EncodeTrackVolume(33, 0.25); }), "OSC track 33 blocked");
Assert(RejectBinding(() => { _ = OscCodec.EncodeTrackVolume(1, double.NaN); }), "OSC nonfinite volume blocked");

using var reaperFake = new System.Net.Sockets.UdpClient(
    new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
int fakeReaperPort = ((System.Net.IPEndPoint)reaperFake.Client.LocalEndPoint!).Port;
int localPort;
using (var probe = new System.Net.Sockets.UdpClient(
    new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
    localPort = ((System.Net.IPEndPoint)probe.Client.LocalEndPoint!).Port;
await using (var adapter = new ReaperOscClient())
{
    var feedback = new TaskCompletionSource<(int track, double value)>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    adapter.VolumeFeedback += (track, value) => feedback.TrySetResult((track, value));
    adapter.Start(fakeReaperPort, localPort);
    Assert(adapter.Listening && !adapter.HasFeedback, "UDP bound but not falsely Verified");
    await adapter.WriteTrackVolumeAsync(2, 0.4);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    var received = await reaperFake.ReceiveAsync(timeout.Token);
    Assert(OscCodec.TryDecodeTrackVolume(received.Buffer, out var incomingTrack, out var incomingValue)
        && incomingTrack == 2 && Math.Abs(incomingValue - .4) < 1e-5, "adapter UDP sends real OSC binary");
    await reaperFake.SendAsync(OscCodec.EncodeTrackVolume(2, 0.7).AsMemory(),
        new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, localPort), timeout.Token);
    var confirmation = await feedback.Task.WaitAsync(timeout.Token);
    Assert(confirmation.track == 2 && Math.Abs(confirmation.value - .7) < 1e-5
        && adapter.HasFeedback, "adapter receives genuine UDP OSC feedback");
}

Console.WriteLine($"PASS: {count} protocol / simulator / multi-adapter assertions");
