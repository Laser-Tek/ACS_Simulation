using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcsHal.Core;

public enum ConnectionMode { Simulator, Ethernet }

public enum AxisKind { Linear, Rotary, Door }

public sealed record ConnectionConfig
{
    public ConnectionMode Mode { get; init; } = ConnectionMode.Simulator;
    public string Address { get; init; } = "10.0.0.100";

    /// <summary>ACS controllers listen for TCP connections on port 701.</summary>
    public int Port { get; init; } = 701;

    /// <summary>Per-call communication timeout; null keeps the library default.</summary>
    public int? TimeoutMs { get; init; }
}

public sealed record AxisConfig
{
    public required string Name { get; init; }
    public required int Index { get; init; }
    public AxisKind Kind { get; init; } = AxisKind.Linear;

    /// <summary>Drive mechanics; null while not yet known (scaling is then not checked).</summary>
    public AxisMechanics? Mechanics { get; init; }

    public MotionProfile? Motion { get; init; }
}

/// <summary>
/// A dual-drive pair run in ACS true-gantry mode (MFLAGS.#GANTRY). Once enabled, the controller axis of
/// <see cref="CogAxis"/> moves both motors (centre of gravity) and the axis of <see cref="YawAxis"/>
/// holds the difference between them.
/// </summary>
public sealed record GantryConfig
{
    public required string Name { get; init; }
    public required string CogAxis { get; init; }
    public required string YawAxis { get; init; }
}

public sealed record MachineConfig
{
    public ConnectionConfig Connection { get; init; } = new();
    public IReadOnlyList<AxisConfig> Axes { get; init; } = [];
    public IReadOnlyList<GantryConfig> Gantries { get; init; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Lets "type" appear anywhere in a mechanics object, not only first.
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static MachineConfig Load(string path)
    {
        var config = JsonSerializer.Deserialize<MachineConfig>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"{path} is empty.");
        config.Validate();
        return config;
    }

    public AxisConfig Axis(string name) =>
        Axes.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"No axis named '{name}' in the machine config.");

    public void Validate()
    {
        var duplicateName = Axes.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicateName is not null)
            throw new InvalidDataException($"Axis name '{duplicateName.Key}' is used more than once.");

        var duplicateIndex = Axes.GroupBy(a => a.Index).FirstOrDefault(g => g.Count() > 1);
        if (duplicateIndex is not null)
            throw new InvalidDataException($"Axis index {duplicateIndex.Key} is used by {string.Join(", ", duplicateIndex.Select(a => a.Name))}.");

        foreach (var axis in Axes)
            axis.Mechanics?.Validate(axis.Name);

        foreach (var gantry in Gantries)
        {
            var cog = Axis(gantry.CogAxis);
            var yaw = Axis(gantry.YawAxis);
            if (cog.Index == yaw.Index)
                throw new InvalidDataException($"Gantry '{gantry.Name}' uses the same axis for COG and yaw.");
            // True gantry averages and differences the two encoders, so both sides must scale identically.
            if (cog.Mechanics != yaw.Mechanics)
                throw new InvalidDataException($"Gantry '{gantry.Name}': {cog.Name} and {yaw.Name} must have identical mechanics.");
        }

        var sharedAxis = Gantries.SelectMany(g => new[] { g.CogAxis, g.YawAxis })
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (sharedAxis is not null)
            throw new InvalidDataException($"Axis '{sharedAxis.Key}' belongs to more than one gantry.");
    }
}
