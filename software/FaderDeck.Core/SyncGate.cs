namespace FaderDeck.Core;

/// <summary>
/// Conservative host-side gate. Cached/unknown values are not authoritative
/// and must never automatically reposition a motor.
/// </summary>
public enum ValueConfidence
{
    Unknown,
    Cached,
    Verified
}

public readonly record struct ParameterState(double Normalized, ValueConfidence Confidence);

public sealed class SyncGate
{
    private readonly Dictionary<string, ParameterState> states = new(StringComparer.Ordinal);

    public void Observe(string key, double normalized, ValueConfidence confidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!double.IsFinite(normalized) || normalized < 0 || normalized > 1)
            throw new ArgumentOutOfRangeException(nameof(normalized), "Expected normalized value 0..1.");
        states[key] = new ParameterState(normalized, confidence);
    }

    public void Disconnect()
    {
        foreach (string key in states.Keys.ToArray())
        {
            ParameterState previous = states[key];
            states[key] = previous with { Confidence = ValueConfidence.Unknown };
        }
    }

    public bool TryGetMotorTarget(string key, bool isTouched, out byte position)
    {
        position = 0;
        if (isTouched || !states.TryGetValue(key, out ParameterState parameter)
            || parameter.Confidence != ValueConfidence.Verified)
            return false;
        position = (byte)Math.Clamp((int)Math.Round(parameter.Normalized * 255, MidpointRounding.AwayFromZero), 0, 255);
        return true;
    }
}
