using System.Globalization;
using System.IO.Ports;
using FaderDeck.Core;

static void Help()
{
    Console.WriteLine("FaderDeck M0 diagnostics");
    Console.WriteLine("Usage: faderdeck ports | <PORT> ping|state|layer <0..7>|move <layer> <position> <speed>|calibrate");
    Console.WriteLine("WARNING: move/calibrate will actuate real motor hardware.");
}

if (args.Length == 1 && args[0].Equals("ports", StringComparison.OrdinalIgnoreCase))
{
    string[] ports = SerialPort.GetPortNames();
    Console.WriteLine(ports.Length == 0 ? "No serial ports detected." : string.Join(Environment.NewLine, ports));
    return;
}
if (args.Length < 2 || args[0] is "--help" or "-h")
{
    Help();
    return;
}

string command;
try
{
    static byte ByteArg(string arg) => byte.Parse(arg, NumberStyles.None, CultureInfo.InvariantCulture);
    command = args[1].ToLowerInvariant() switch
    {
        "ping" when args.Length == 2 => "PING",
        "state" when args.Length == 2 => "STATE",
        "layer" when args.Length == 3 => DeviceProtocol.Layer(ByteArg(args[2])),
        "move" when args.Length == 5 => DeviceProtocol.Move(ByteArg(args[2]), ByteArg(args[3]), ByteArg(args[4])),
        "calibrate" when args.Length == 2 => "CALIBRATE",
        _ => throw new ArgumentException("Invalid command or argument count.")
    };
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
        ReadTimeout = 1500,
        WriteTimeout = 1500
    };
    port.Open();
    port.DiscardInBuffer();
    port.WriteLine(command);
    string reply = "";
    // Development boards can also emit boot/log lines. Ignore unrelated text.
    for (int attempt = 0; attempt < 20; attempt++)
    {
        reply = port.ReadLine().Trim();
        if (reply.StartsWith("PONG ", StringComparison.Ordinal)
            || reply.StartsWith("STATE ", StringComparison.Ordinal)
            || reply.StartsWith("OK ", StringComparison.Ordinal)
            || reply.StartsWith("ERR ", StringComparison.Ordinal))
            break;
    }
    Console.WriteLine(reply);
    if (reply.StartsWith("ERR ", StringComparison.Ordinal))
        Environment.ExitCode = 1;
    else if (command == "STATE" && !DeviceProtocol.TryParseState(reply, out _))
        Environment.ExitCode = 1;
    else if (command == "PING" && reply != "PONG 1")
        Environment.ExitCode = 1;
    else if (command != "STATE" && command != "PING" && !reply.StartsWith("OK ", StringComparison.Ordinal))
        Environment.ExitCode = 1;
}
catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"Device communication failed: {e.Message}");
    Environment.ExitCode = 1;
}
