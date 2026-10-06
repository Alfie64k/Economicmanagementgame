using System.Text.Json;

namespace Sim.Core.Events;

public sealed class EffectDef
{
    public string Kind { get; set; } = "";   // level | state | mod | global | imf | default | election | gameover
    public string Key { get; set; } = "";
    public double Value { get; set; }
    public int Months { get; set; }
    public string Scale { get; set; } = "none";
}

public sealed class ChoiceDef
{
    public string Label { get; set; } = "";
    public double Cost { get; set; }
    public List<EffectDef> Effects { get; set; } = new();
}

public sealed class NextDef { public string Id { get; set; } = ""; public double Prob { get; set; } public int Delay { get; set; } }
public sealed class ProbMod { public string Metric { get; set; } = ""; public double Ref { get; set; } public double Slope { get; set; } }

public sealed class EventDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public string Scope { get; set; } = "country";
    public double Prob { get; set; }
    public List<string> Cond { get; set; } = new();
    public List<ProbMod> ProbMods { get; set; } = new();
    public List<EffectDef> Effects { get; set; } = new();
    public List<ChoiceDef> Choices { get; set; } = new();
    public int DefaultChoice { get; set; }
    public int Deadline { get; set; } = 3;
    public List<NextDef> Next { get; set; } = new();
    public int Cooldown { get; set; } = 36;
    public string Category { get; set; } = "shock";
    public bool AllCountries { get; set; }
}

public sealed class EventFile { public List<EventDef> Events { get; set; } = new(); }

public static class EventCatalog
{
    static EventFile? _f;
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };
    public static IReadOnlyList<EventDef> Events => (_f ??= Load()).Events;
    public static EventDef? Find(string id) => Events.FirstOrDefault(e => e.Id == id);

    static EventFile Load()
    {
        using var s = typeof(EventCatalog).Assembly.GetManifestResourceStream("data/events.json")
            ?? throw new InvalidOperationException("Embedded data/events.json not found");
        return JsonSerializer.Deserialize<EventFile>(s, Opts) ?? throw new InvalidDataException("bad events.json");
    }
}
