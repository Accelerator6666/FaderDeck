using System.Globalization;

namespace FaderDeck.Core;

/// <summary>
/// Software-only, deterministic emulator of the FaderDeck M0 ASCII serial command
/// endpoint. It does not model electronics, torque, latency, I2C or motor dynamics.
/// Emulator-only fault/touch controls are not part of the firmware wire protocol.
/// </summary>
public sealed class VirtualFader
{
    private readonly byte[] layerTargets = new byte[8];
    private byte mode = 2; // FaderBuddy MODE_INPUT_IDLE
    private byte position = 128;
    private byte activeLayer;
    private bool touched;

    public bool Connected { get; private set; } = true;
    public byte ProtocolVersion { get; private set; } = 5;
    public byte FirmwareMajor { get; } = 1;
    public byte FirmwareMinor { get; } = 5;
    public string Serial { get; } = "FADEB0DD000000000001";
    public FaderState State => new(mode, activeLayer, position, touched);

    public VirtualFader()
    {
        Array.Fill(layerTargets, (byte)128);
    }

    public void SetConnected(bool value)
    {
        Connected = value;
    }

    public void SetProtocol(byte version)
    {
        ProtocolVersion = version;
    }

    public void SetTouch(bool value)
    {
        touched = value;
        if (mode is 1 or 2) mode = value ? (byte)1 : (byte)2;
    }

    public void ManualMove(byte next)
    {
        if (!Connected || !touched || mode != 1)
            throw new InvalidOperationException("Manual movement requires connected, touched input mode.");
        position = next;
        layerTargets[activeLayer] = next;
    }

    public void SetBusy(bool value)
    {
        mode = value ? (byte)0 : (touched ? (byte)1 : (byte)2);
    }

    public void SetFault(bool value)
    {
        mode = value ? (byte)3 : (touched ? (byte)1 : (byte)2);
    }

    public void FinishCalibration()
    {
        if (mode != 4)
            throw new InvalidOperationException("No calibration is in progress.");
        mode = touched ? (byte)1 : (byte)2;
    }

    public byte GetLayerTarget(byte layer)
    {
        if (layer > 7) throw new ArgumentOutOfRangeException(nameof(layer));
        return layerTargets[layer];
    }

    public string Execute(string? request)
    {
        if (request is null) return "ERR UNKNOWN_COMMAND";
        string[] p = request.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) return "ERR UNKNOWN_COMMAND";
        if (p.Length == 1 && p[0] == "PING") return "PONG 1";
        if (!Connected) return "ERR I2C_READ";
        if (p.Length == 1 && p[0] == "INFO")
            return FormattableString.Invariant($"INFO {ProtocolVersion} {FirmwareMajor} {FirmwareMinor} {Serial}");
        if (p.Length == 1 && p[0] == "STATE")
        {
            if (ProtocolVersion != 5) return "ERR UNSUPPORTED_PROTOCOL";
            return FormattableString.Invariant($"STATE {mode} {activeLayer} {position} {(touched ? 1 : 0)}");
        }

        switch (p[0])
        {
            case "MOVE":
                if (p.Length != 4 || !TryByte(p[1], out byte layer) || layer > 7
                    || !TryByte(p[2], out byte target) || !TryByte(p[3], out _))
                    return "ERR INVALID_MOVE";
                if (ProtocolVersion != 5) return "ERR UNSUPPORTED_PROTOCOL";
                if (touched || mode != 2) return "ERR FADER_BUSY_OR_TOUCHED";
                layerTargets[layer] = target;
                // Instantaneous target acquisition is intentional: NO simulated motor physics.
                if (layer == activeLayer) position = target;
                return "OK MOVE";

            case "LAYER":
                if (p.Length != 2 || !TryByte(p[1], out byte nextLayer) || nextLayer > 7)
                    return "ERR INVALID_LAYER";
                if (ProtocolVersion != 5) return "ERR UNSUPPORTED_PROTOCOL";
                if (touched || mode != 2) return "ERR FADER_BUSY_OR_TOUCHED";
                activeLayer = nextLayer;
                position = layerTargets[nextLayer];
                return "OK LAYER";

            case "CALIBRATE":
                if (p.Length != 1) return "ERR UNKNOWN_COMMAND";
                if (ProtocolVersion != 5) return "ERR UNSUPPORTED_PROTOCOL";
                if (touched || mode != 2) return "ERR FADER_BUSY_OR_TOUCHED";
                mode = 4; // simulator stays in calibration until FinishCalibration()
                return "OK CALIBRATE";

            default:
                return "ERR UNKNOWN_COMMAND";
        }
    }

    private static bool TryByte(string input, out byte output)
    {
        return byte.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out output);
    }
}
