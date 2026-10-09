using System.Globalization;

namespace FaderDeck.Core;

public readonly record struct FaderState(byte Mode, byte Layer, byte Position, bool Touch);

public static class DeviceProtocol
{
    public static string Move(byte layer, byte position, byte speed)
    {
        if (layer > 7) throw new ArgumentOutOfRangeException(nameof(layer));
        return FormattableString.Invariant($"MOVE {layer} {position} {speed}");
    }

    public static string Layer(byte layer)
    {
        if (layer > 7) throw new ArgumentOutOfRangeException(nameof(layer));
        return FormattableString.Invariant($"LAYER {layer}");
    }

    public static bool TryParseState(string line, out FaderState state)
    {
        state = default;
        if (line is null) return false;
        string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "STATE") return false;
        if (!byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out byte mode) || mode > 4
            || !byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out byte layer) || layer > 7
            || !byte.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out byte position)
            || !byte.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out byte touch) || touch > 1)
            return false;
        state = new FaderState(mode, layer, position, touch == 1);
        return true;
    }
}
