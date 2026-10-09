using FaderDeck.Core;
using System.Buffers.Binary;
using System.Text;

namespace FaderDeck.Reaper;

/// <summary>Minimal OSC 1.0 single float32 support for ReaperOSC TRACK_VOLUME n.</summary>
public static class OscCodec
{
    private static int Padded(int count) => (count + 4) & ~3;

    public static byte[] EncodeFloat(string address, float value)
    {
        if (string.IsNullOrWhiteSpace(address) || address[0] != '/' || address.Any(c => c > 127 || c == '\0'))
            throw new ArgumentException("An ASCII OSC address starting with / is required.", nameof(address));
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        int addressBytes = Encoding.ASCII.GetByteCount(address) + 1;
        int dataOffset = Padded(addressBytes) + 4; // typetag ",f" padded to 4
        var result = new byte[dataOffset + 4];
        Encoding.ASCII.GetBytes(address, result.AsSpan());
        result[Padded(addressBytes)] = (byte)',';
        result[Padded(addressBytes) + 1] = (byte)'f';
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(dataOffset, 4),
            BitConverter.SingleToInt32Bits(value));
        return result;
    }

    private static bool ReadString(ReadOnlySpan<byte> input, ref int offset, out string value)
    {
        value = "";
        if (offset >= input.Length) return false;
        int end = input[offset..].IndexOf((byte)0);
        if (end <= 0) return false;
        ReadOnlySpan<byte> bytes = input.Slice(offset, end);
        if (bytes.IndexOfAnyExceptInRange((byte)0x20, (byte)0x7e) >= 0) return false;
        value = Encoding.ASCII.GetString(bytes);
        offset += Padded(end + 1);
        return offset <= input.Length;
    }

    public static bool TryDecodeFloat(ReadOnlySpan<byte> data, out string address, out float value)
    {
        address = "";
        value = 0;
        int index = 0;
        if (!ReadString(data, ref index, out address) || !address.StartsWith('/'))
            return false;
        if (!ReadString(data, ref index, out var type) || type != ",f" || data.Length != index + 4)
            return false;
        float result = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32BigEndian(data.Slice(index, 4)));
        if (!float.IsFinite(result)) return false;
        value = result;
        return true;
    }

    public static byte[] EncodeTrackVolume(int track, double normalized)
    {
        if (track is < 1 or > ControlProfiles.MaxReaperTrack) throw new ArgumentOutOfRangeException(nameof(track));
        if (!double.IsFinite(normalized) || normalized is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(normalized));
        return EncodeFloat($"/track/{track}/volume", (float)normalized);
    }

    public static bool TryDecodeTrackVolume(ReadOnlySpan<byte> datagram, out int track, out double normalized)
    {
        track = 0;
        normalized = 0;
        if (!TryDecodeFloat(datagram, out var address, out float raw)) return false;
        const string prefix = "/track/";
        const string suffix = "/volume";
        if (!address.StartsWith(prefix, StringComparison.Ordinal)
            || !address.EndsWith(suffix, StringComparison.Ordinal))
            return false;
        var digits = address.AsSpan(prefix.Length, address.Length - prefix.Length - suffix.Length);
        if (!int.TryParse(digits, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out track)
            || track is < 1 or > ControlProfiles.MaxReaperTrack
            || raw is < 0 or > 1)
            return false;
        normalized = raw;
        return true;
    }
}
