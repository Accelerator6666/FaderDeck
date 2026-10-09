using System.Globalization;
using System.IO.Ports;
using FaderDeck.Core;

static bool SimLine(VirtualFader sim, string line)
{
    string[] p = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (p.Length == 0) return true;
    static bool OnOff(string[] p, out bool value)
    {
        value = false;
        if (p.Length != 2) return false;
        if (p[1].Equals("on", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
        if (p[1].Equals("off", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    try
    {
        switch (p[0].ToLowerInvariant())
        {
            case ":quit":
            case ":exit":
                return false;
            case ":help":
                Console.WriteLine("Firmware-compatible: PING, INFO, STATE, LAYER n, MOVE layer position speed, CALIBRATE");
                Console.WriteLine("Emulator-only: :touch on|off, :manual 0..255, :busy on|off, :fault on|off, :connect on|off, :protocol 0..255, :caldone, :quit");
                return true;
            case ":touch":
                if (!OnOff(p, out bool touch)) break;
                sim.SetTouch(touch);
                Console.WriteLine($"SIM Touch={touch}");
                return true;
            case ":busy":
                if (!OnOff(p, out bool busy)) break;
                sim.SetBusy(busy);
                Console.WriteLine($"SIM Busy={busy}");
                return true;
            case ":fault":
                if (!OnOff(p, out bool fault)) break;
                sim.SetFault(fault);
                Console.WriteLine($"SIM Fault={fault}");
                return true;
            case ":connect":
                if (!OnOff(p, out bool connected)) break;
                sim.SetConnected(connected);
                Console.WriteLine($"SIM Connected={connected}");
                return true;
            case ":manual":
                if (p.Length != 2 || !byte.TryParse(p[1], out byte position)) break;
                sim.ManualMove(position);
                Console.WriteLine($"SIM Manual position={position}");
                return true;
            case ":protocol":
                if (p.Length != 2 || !byte.TryParse(p[1], out byte protocol)) break;
                sim.SetProtocol(protocol);
                Console.WriteLine($"SIM I2C protocol={protocol}");
                return true;
            case ":caldone":
                if (p.Length != 1) break;
                sim.FinishCalibration();
                Console.WriteLine("SIM Calibration complete");
                return true;
            default:
                if (p[0].StartsWith(':')) break;
                Console.WriteLine(sim.Execute(line));
                return true;
        }
    }
    catch (InvalidOperationException e)
    {
        Console.WriteLine("SIM ERROR " + e.Message);
        return true;
    }
    Console.WriteLine("SIM Invalid simulator command. Use :help.");
    return true;
}

static void Simulate(bool demo)
{
    var sim = new VirtualFader();
    Console.WriteLine("FaderDeck Virtual Fader (no hardware / no physical motor simulation)");
    if (demo)
    {
        string[] script = [
            "PING", "INFO", "STATE", "MOVE 0 200 128", "STATE",
            ":touch on", "MOVE 0 50 128", ":manual 155", "STATE",
            ":touch off", "LAYER 1", "STATE", "MOVE 1 45 128",
            "STATE", ":fault on", "MOVE 1 20 128", ":fault off",
            ":connect off", "STATE", ":connect on", "INFO", "STATE"
        ];
        foreach (string line in script)
        {
            Console.WriteLine("> " + line);
            SimLine(sim, line);
        }
        return;
    }

    Console.WriteLine("Enter :help for emulator controls, :quit to exit.");
    string? input;
    while ((input = Console.ReadLine()) != null)
    {
        if (!SimLine(sim, input)) break;
    }
}

static void Help()
{
    Console.WriteLine("FaderDeck M0 diagnostics (USB Serial/JTAG, no software integration yet)");
    Console.WriteLine("Usage:");
    Console.WriteLine("  faderdeck ports");
    Console.WriteLine("  faderdeck sim [--demo]   (no hardware required)");
    Console.WriteLine("  faderdeck <PORT> ping|info|state|diagnose|watch [count]");
    Console.WriteLine("  faderdeck <PORT> layer <0..7>");
    Console.WriteLine("  faderdeck <PORT> move <layer> <position> <speed>");
    Console.WriteLine("  faderdeck <PORT> calibrate");
    Console.WriteLine("WARNING: move/calibrate actuate the physical motor; keep travel clear.");
}

static string Request(SerialPort port, string command)
{
    port.WriteLine(command);
    for (int attempt = 0; attempt < 20; attempt++)
    {
        string reply = port.ReadLine().Trim();
        if (reply.StartsWith("PONG ", StringComparison.Ordinal)
            || reply.StartsWith("INFO ", StringComparison.Ordinal)
            || reply.StartsWith("STATE ", StringComparison.Ordinal)
            || reply.StartsWith("OK ", StringComparison.Ordinal)
            || reply.StartsWith("ERR ", StringComparison.Ordinal))
            return reply;
    }
    throw new IOException("No recognized device response. Check the USB port/firmware.");
}

static bool PrintValidatedReply(string request, string response)
{
    Console.WriteLine(response);
    if (response.StartsWith("ERR ", StringComparison.Ordinal))
        return false;
    if (request == "PING") return response == "PONG 1";
    if (request == "INFO")
    {
        if (!DeviceProtocol.TryParseInfo(response, out var info)) return false;
        Console.WriteLine($"FaderBuddy: I2C protocol v{info.Protocol}, firmware {info.FirmwareMajor}.{info.FirmwareMinor}, serial {info.Serial}");
        if (info.Protocol != 5)
        {
            Console.Error.WriteLine("Firmware I2C protocol is not v5; firmware commands are blocked.");
            return false;
        }
        return true;
    }
    if (request == "STATE")
    {
        if (!DeviceProtocol.TryParseState(response, out var state)) return false;
        Console.WriteLine($"Mode={state.Mode} Layer={state.Layer} Position={state.Position} Touch={state.Touch}");
        return true;
    }
    return response == "OK " + request.Split(' ', 2)[0];
}

if (args.Length > 0 && args[0].Equals("sim", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length == 1 || (args.Length == 2 && args[1] == "--demo"))
        Simulate(args.Length == 2);
    else
    {
        Help();
        Environment.ExitCode = 2;
    }
    return;
}

if (args.Length == 1 && args[0].Equals("ports", StringComparison.OrdinalIgnoreCase))
{
    string[] ports = SerialPort.GetPortNames();
    Console.WriteLine(ports.Length == 0 ? "No serial ports detected." : string.Join(Environment.NewLine, ports));
    return;
}
if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Help();
    return;
}
if (args.Length < 2)
{
    Help();
    Environment.ExitCode = 2;
    return;
}

string command;
int watchCount = 0;
try
{
    static byte ByteArg(string arg) => byte.Parse(arg, NumberStyles.None, CultureInfo.InvariantCulture);
    command = args[1].ToLowerInvariant() switch
    {
        "ping" when args.Length == 2 => "PING",
        "info" when args.Length == 2 => "INFO",
        "state" when args.Length == 2 => "STATE",
        "diagnose" when args.Length == 2 => "DIAGNOSE",
        "watch" when args.Length == 2 || args.Length == 3 => "WATCH",
        "layer" when args.Length == 3 => DeviceProtocol.Layer(ByteArg(args[2])),
        "move" when args.Length == 5 => DeviceProtocol.Move(ByteArg(args[2]), ByteArg(args[3]), ByteArg(args[4])),
        "calibrate" when args.Length == 2 => "CALIBRATE",
        _ => throw new ArgumentException("Invalid command or argument count.")
    };
    if (command == "WATCH")
    {
        watchCount = args.Length == 3 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 20;
        if (watchCount is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(watchCount), "Count must be 1..200.");
    }
}
catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
{
    Console.Error.WriteLine(e.Message);
    Help();
    Environment.ExitCode = 2;
    return;
}

try
{
    using var port = new SerialPort(args[0], 115200)
    {
        NewLine = "\n",
        ReadTimeout = 2000,
        WriteTimeout = 2000
    };
    port.Open();
    port.DiscardInBuffer();
    if (command is "DIAGNOSE" or "WATCH")
    {
        string[] setup = command == "DIAGNOSE" ? ["PING", "INFO", "STATE"] : ["PING", "INFO"];
        foreach (string step in setup)
        {
            if (!PrintValidatedReply(step, Request(port, step)))
            {
                Environment.ExitCode = 1;
                return;
            }
        }
        if (command == "WATCH")
        {
            for (int i = 0; i < watchCount; i++)
            {
                if (!PrintValidatedReply("STATE", Request(port, "STATE")))
                {
                    Environment.ExitCode = 1;
                    break;
                }
                if (i < watchCount - 1) Thread.Sleep(100);
            }
        }
    }
    else if (!PrintValidatedReply(command, Request(port, command)))
        Environment.ExitCode = 1;
}
catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"Device communication failed: {e.Message}");
    Environment.ExitCode = 1;
}
