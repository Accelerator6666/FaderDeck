using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaderDeck.Core;

/// <summary>Eight banks × four faders. Auto preserves legacy sequential mapping;
/// Explicit binds to an OBS input name; Unassigned blocks control.</summary>
public enum MappingMode { Auto, Explicit, Unassigned }

public readonly record struct FaderBinding(MappingMode Mode, string? InputName = null)
{
    public static FaderBinding Auto => new(MappingMode.Auto);
    public static FaderBinding Unassigned => new(MappingMode.Unassigned);
    public static FaderBinding ForInput(string name) => new(MappingMode.Explicit, name);
}

public sealed class ObsMappingProfile
{
    public const int BankCount = 8;
    public const int SlotCount = 4;
    public const int FormatVersion = 1;

    private readonly FaderBinding[,] bindings = new FaderBinding[BankCount, SlotCount];

    private sealed record Entry(int Bank, int Slot, string Mode, string? InputName);
    private sealed record Document(int Version, Entry[] Mappings);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false
    };

    public ObsMappingProfile()
    {
        for (int bank = 0; bank < BankCount; bank++)
            for (int slot = 0; slot < SlotCount; slot++)
                bindings[bank, slot] = FaderBinding.Auto;
    }

    private static void ValidateLocation(int bank, int slot)
    {
        if (bank is < 0 or >= BankCount) throw new ArgumentOutOfRangeException(nameof(bank));
        if (slot is < 0 or >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
    }

    public FaderBinding Get(int bank, int slot)
    {
        ValidateLocation(bank, slot);
        return bindings[bank, slot];
    }

    public void Set(int bank, int slot, FaderBinding binding)
    {
        ValidateLocation(bank, slot);
        if (binding.Mode == MappingMode.Explicit)
        {
            if (string.IsNullOrWhiteSpace(binding.InputName) ||
                binding.InputName.Length > 256 ||
                binding.InputName.Any(char.IsControl))
                throw new ArgumentException("An explicit binding requires a valid OBS input name (1..256 chars).", nameof(binding));
        }
        else if (binding.Mode is not (MappingMode.Auto or MappingMode.Unassigned)
                 || binding.InputName is not null)
            throw new ArgumentException("Invalid binding mode or unexpected input name.", nameof(binding));

        bindings[bank, slot] = binding;
    }

    public void ResetBank(int bank)
    {
        ValidateLocation(bank, 0);
        for (int slot = 0; slot < SlotCount; slot++)
            bindings[bank, slot] = FaderBinding.Auto;
    }

    /// <summary>No fallback if a named source is absent: fail closed.</summary>
    public string? Resolve(int bank, int slot, IReadOnlyList<string> availableInputs)
    {
        ArgumentNullException.ThrowIfNull(availableInputs);
        FaderBinding binding = Get(bank, slot);
        return binding.Mode switch
        {
            MappingMode.Auto =>
                bank * SlotCount + slot < availableInputs.Count
                    ? availableInputs[bank * SlotCount + slot] : null,
            MappingMode.Explicit =>
                availableInputs.FirstOrDefault(x => string.Equals(x, binding.InputName, StringComparison.Ordinal)),
            _ => null
        };
    }

    public string ToJson()
    {
        var items = new List<Entry>();
        for (int bank = 0; bank < BankCount; bank++)
            for (int slot = 0; slot < SlotCount; slot++)
            {
                FaderBinding binding = bindings[bank, slot];
                if (binding.Mode != MappingMode.Auto)
                    items.Add(new Entry(bank, slot, binding.Mode.ToString(), binding.InputName));
            }
        return JsonSerializer.Serialize(new Document(FormatVersion, items.ToArray()), JsonOptions);
    }

    public static ObsMappingProfile FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 131072) throw new InvalidDataException("Profile is too large.");
        Document document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(json, JsonOptions)
                ?? throw new InvalidDataException("Empty mapping document.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("Malformed OBS mapping profile.", e);
        }
        if (document.Version != FormatVersion)
            throw new InvalidDataException($"Unsupported profile schema version: {document.Version}");
        if (document.Mappings is null || document.Mappings.Length > BankCount * SlotCount)
            throw new InvalidDataException("Invalid mapping count.");

        var profile = new ObsMappingProfile();
        var seen = new HashSet<(int bank, int slot)>();
        foreach (Entry? entry in document.Mappings)
        {
            if (entry is null || !seen.Add((entry.Bank, entry.Slot)))
                throw new InvalidDataException("Null or duplicate binding.");
            MappingMode mode = entry.Mode switch
            {
                "Auto" => MappingMode.Auto,
                "Explicit" => MappingMode.Explicit,
                "Unassigned" => MappingMode.Unassigned,
                _ => throw new InvalidDataException("Unknown mapping mode.")
            };
            try { profile.Set(entry.Bank, entry.Slot, new FaderBinding(mode, entry.InputName)); }
            catch (ArgumentException e) { throw new InvalidDataException("Invalid OBS mapping entry.", e); }
        }
        return profile;
    }

    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FaderDeck", "obs-mappings.json");

    public static ObsMappingProfile Load(string path)
    {
        return !File.Exists(path)
            ? new ObsMappingProfile()
            : FromJson(File.ReadAllText(path));
    }

    /// <summary>Atomic same-directory replacement; no OBS credentials are included.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string parent = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("A directory is required.", nameof(path));
        Directory.CreateDirectory(parent);
        string temp = Path.Combine(parent, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, ToJson());
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
