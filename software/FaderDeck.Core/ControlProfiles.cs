using System.Text.Json;

namespace FaderDeck.Core;

/// <summary>
/// Adapter-independent mapping store: a control binds to an adapter's semantic
/// parameter ID and a target. Missing bindings retain adapter-defined Auto.
/// No authentication material is stored.
/// </summary>
public enum ProfileBindingMode { Auto, Explicit, Unassigned }

public readonly record struct ControlBinding(ProfileBindingMode Mode, string? ParameterId = null, string? Target = null)
{
    public static ControlBinding Auto => new(ProfileBindingMode.Auto);
    public static ControlBinding Unassigned => new(ProfileBindingMode.Unassigned);
    public static ControlBinding Bind(string parameterId, string target)
        => new(ProfileBindingMode.Explicit, parameterId, target);
}

public readonly record struct ResolvedControl(string AdapterId, string ParameterId, string Target);

public readonly record struct AdapterCapability(string AdapterId, string ParameterId,
    bool CanWrite, bool CanRead, bool HasFeedback);

public static class ControlAdapters
{
    public const string Obs = "obs";
    public const string Reaper = "reaper";
    public const string Davinci = "davinci";

    private static readonly IReadOnlyDictionary<(string adapter, string parameter), AdapterCapability> capabilities =
        new Dictionary<(string, string), AdapterCapability>
        {
            [(Obs, "input.volume")] = new(Obs, "input.volume", true, true, true),
            [(Reaper, "track.volume")] = new(Reaper, "track.volume", true, false, true),
            // The public Resolve scripting API does not expose arbitrary
            // lift/gamma/gain/contrast parameter read/write. Placeholder only.
            [(Davinci, "primaries.contrast")] = new(Davinci, "primaries.contrast", false, false, false),
            [(Davinci, "primaries.saturation")] = new(Davinci, "primaries.saturation", false, false, false)
        };

    public static AdapterCapability Get(string adapter, string parameter)
    {
        if (!capabilities.TryGetValue((adapter, parameter), out var cap))
            throw new ArgumentException($"Unsupported binding {adapter}/{parameter}");
        return cap;
    }

    public static IReadOnlyList<AdapterCapability> List(string adapter)
        => capabilities.Values.Where(x => x.AdapterId == adapter).OrderBy(x => x.ParameterId).ToArray();
}

public sealed class ControlProfiles
{
    public const int Version = 1;
    public const int Banks = 8;
    public const int Slots = 4;
    public const int MaxReaperTrack = Banks * Slots;

    private readonly Dictionary<(string app, int bank, int slot), ControlBinding> overrides = new();

    private sealed record Entry(string App, int Bank, int Slot, string Mode, string? ParameterId, string? Target);
    private sealed record Document(int Version, Entry[] Bindings);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static void ValidateLocation(string app, int bank, int slot)
    {
        if (app is not (ControlAdapters.Obs or ControlAdapters.Reaper or ControlAdapters.Davinci))
            throw new ArgumentException("Unknown adapter ID.", nameof(app));
        if (bank is < 0 or >= Banks) throw new ArgumentOutOfRangeException(nameof(bank));
        if (slot is < 0 or >= Slots) throw new ArgumentOutOfRangeException(nameof(slot));
    }

    public ControlBinding Get(string app, int bank, int slot)
    {
        ValidateLocation(app, bank, slot);
        return overrides.TryGetValue((app, bank, slot), out var value) ? value : ControlBinding.Auto;
    }

    public void Set(string app, int bank, int slot, ControlBinding value)
    {
        ValidateLocation(app, bank, slot);
        if (value.Mode == ProfileBindingMode.Explicit)
        {
            if (string.IsNullOrWhiteSpace(value.ParameterId) || string.IsNullOrWhiteSpace(value.Target))
                throw new ArgumentException("Explicit mappings need a parameter and target.");
            AdapterCapability cap = ControlAdapters.Get(app, value.ParameterId);
            if (!cap.CanWrite) throw new ArgumentException("Adapter does not support controlling this parameter.");
            if (value.Target.Length > 256 || value.Target.Any(char.IsControl))
                throw new ArgumentException("Invalid target identifier.");
            if (app == ControlAdapters.Reaper &&
                (!int.TryParse(value.Target, out int track) || track is < 1 or > MaxReaperTrack ||
                 track.ToString(System.Globalization.CultureInfo.InvariantCulture) != value.Target))
                throw new ArgumentException("REAPER track target must be 1..32.");
        }
        else if (value.Mode is not (ProfileBindingMode.Auto or ProfileBindingMode.Unassigned)
                 || value.ParameterId is not null || value.Target is not null)
            throw new ArgumentException("Non-explicit mappings cannot carry a parameter or target.");

        var key = (app, bank, slot);
        if (value.Mode == ProfileBindingMode.Auto) overrides.Remove(key);
        else overrides[key] = value;
    }

    public void ResetBank(string app, int bank)
    {
        ValidateLocation(app, bank, 0);
        for (int slot = 0; slot < Slots; slot++)
            overrides.Remove((app, bank, slot));
    }

    /// <summary>Fail closed if an OBS name is missing or a parameter unsupported.</summary>
    public ResolvedControl? Resolve(string app, int bank, int slot, IReadOnlyList<string>? obsNames = null)
    {
        var value = Get(app, bank, slot);
        if (value.Mode == ProfileBindingMode.Unassigned) return null;
        if (app == ControlAdapters.Davinci) return null;
        if (app == ControlAdapters.Obs)
        {
            if (obsNames is null) return null;
            string? source = value.Mode == ProfileBindingMode.Explicit
                ? obsNames.FirstOrDefault(x => x == value.Target)
                : bank * Slots + slot < obsNames.Count ? obsNames[bank * Slots + slot] : null;
            return source is null ? null : new(app, "input.volume", source);
        }
        string target = value.Mode == ProfileBindingMode.Explicit
            ? value.Target!
            : (bank * Slots + slot + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new(app, "track.volume", target);
    }

    public string ToJson()
    {
        Entry[] entries = overrides.OrderBy(x => x.Key.app).ThenBy(x => x.Key.bank).ThenBy(x => x.Key.slot)
            .Select(x => new Entry(x.Key.app, x.Key.bank, x.Key.slot, x.Value.Mode.ToString(),
                x.Value.ParameterId, x.Value.Target)).ToArray();
        return JsonSerializer.Serialize(new Document(Version, entries), Options);
    }

    public static ControlProfiles FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 131072) throw new InvalidDataException("Profile is too large.");
        Document document;
        try { document = JsonSerializer.Deserialize<Document>(json, Options)
            ?? throw new InvalidDataException("Null profile."); }
        catch (JsonException e) { throw new InvalidDataException("Malformed profile JSON.", e); }
        if (document.Version != Version || document.Bindings is null || document.Bindings.Length > Banks * Slots * 3)
            throw new InvalidDataException("Unsupported profile version or excessive bindings.");
        var store = new ControlProfiles();
        var seen = new HashSet<(string, int, int)>();
        foreach (Entry? entry in document.Bindings)
        {
            if (entry is null || !seen.Add((entry.App, entry.Bank, entry.Slot)))
                throw new InvalidDataException("Null or duplicate binding.");
            ProfileBindingMode mode = entry.Mode switch
            {
                "Auto" => ProfileBindingMode.Auto,
                "Explicit" => ProfileBindingMode.Explicit,
                "Unassigned" => ProfileBindingMode.Unassigned,
                _ => throw new InvalidDataException("Unknown binding mode.")
            };
            try { store.Set(entry.App, entry.Bank, entry.Slot,
                    new ControlBinding(mode, entry.ParameterId, entry.Target)); }
            catch (ArgumentException e) { throw new InvalidDataException("Invalid binding.", e); }
        }
        return store;
    }

    /// <summary>Import old OBS mapping overrides; never change/delete legacy file.</summary>
    public static ControlProfiles ImportLegacyObs(ObsMappingProfile legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        var store = new ControlProfiles();
        for (int bank = 0; bank < Banks; bank++)
            for (int slot = 0; slot < Slots; slot++)
            {
                FaderBinding binding = legacy.Get(bank, slot);
                if (binding.Mode == MappingMode.Explicit)
                    store.Set(ControlAdapters.Obs, bank, slot, ControlBinding.Bind("input.volume", binding.InputName!));
                else if (binding.Mode == MappingMode.Unassigned)
                    store.Set(ControlAdapters.Obs, bank, slot, ControlBinding.Unassigned);
            }
        return store;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FaderDeck", "profiles.json");

    public static ControlProfiles LoadOrImport(string path, string legacyObsPath)
    {
        if (File.Exists(path)) return FromJson(File.ReadAllText(path));
        return File.Exists(legacyObsPath)
            ? ImportLegacyObs(ObsMappingProfile.Load(legacyObsPath)) : new ControlProfiles();
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string parent = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("Invalid directory.", nameof(path));
        Directory.CreateDirectory(parent);
        string temp = Path.Combine(parent, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, ToJson());
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
