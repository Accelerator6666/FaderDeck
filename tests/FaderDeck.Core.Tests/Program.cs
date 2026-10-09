using FaderDeck.Core;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

Assert(DeviceProtocol.Move(0, 128, 255) == "MOVE 0 128 255", "motor frame");
Assert(DeviceProtocol.Layer(7) == "LAYER 7", "layer frame");
Assert(DeviceProtocol.TryParseState("STATE 2 0 128 1", out var state), "state parse");
Assert(state.Mode == 2 && state.Layer == 0 && state.Position == 128 && state.Touch, "state values");
Assert(!DeviceProtocol.TryParseState("STATE 2 8 128 1", out _), "invalid layer rejection");
Assert(!DeviceProtocol.TryParseState("STATE 2 0 256 1", out _), "out of range rejection");
Assert(!DeviceProtocol.TryParseState("STATE 2 0 128 2", out _), "invalid touch rejection");

var gate = new SyncGate();
Assert(!gate.TryGetMotorTarget("track.1.volume", false, out _), "unknown value must not actuate");
gate.Observe("track.1.volume", 0.5, ValueConfidence.Cached);
Assert(!gate.TryGetMotorTarget("track.1.volume", false, out _), "cached value must not actuate");
gate.Observe("track.1.volume", 0.5, ValueConfidence.Verified);
Assert(gate.TryGetMotorTarget("track.1.volume", false, out byte value) && value == 128, "verified midpoint");
Assert(!gate.TryGetMotorTarget("track.1.volume", true, out _), "touch blocks motor");
gate.Disconnect();
Assert(!gate.TryGetMotorTarget("track.1.volume", false, out _), "disconnect invalidates feedback");
Console.WriteLine("PASS: 11 core protocol/safety assertions");
