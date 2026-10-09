using System.Security.Cryptography;
using System.Text;

namespace FaderDeck.Obs;

/// <summary>Pure OBS WebSocket v5 conversion/authentication helpers.</summary>
public static class ObsProtocol
{
    public static string Authentication(string password, string salt, string challenge)
    {
        string secret = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    // Preview mixer range: -60..0 dB, with 0% as complete silence.
    // Does not claim to represent OBS's exact built-in UI fader curve.
    public static double SliderToDb(double percent)
        => -60 + Math.Clamp(percent, 0, 100) * 0.6;

    public static double DbToSlider(double db)
        => double.IsFinite(db) ? Math.Clamp((db + 60) / 0.6, 0, 100) : 0;

    public static double SliderToMul(double percent)
        => percent <= 0 ? 0 : Math.Pow(10, SliderToDb(percent) / 20);

    public static bool IsAllowedEndpoint(Uri uri)
    {
        if (uri.Scheme is not ("ws" or "wss")) return false;
        return uri.Scheme == "wss" || uri.IsLoopback;
    }
}
