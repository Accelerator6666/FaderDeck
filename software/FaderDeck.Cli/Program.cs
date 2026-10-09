using System.Globalization;
using System.IO.Ports;
using FaderDeck.Core;

static void Help()
{
    Console.WriteLine("FaderDeck M0 diagnostics (USB Serial/JTAG, no software integration yet)");
    Console.WriteLine("Usage:");
    Console.WriteLine("  faderdeck ports");
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
