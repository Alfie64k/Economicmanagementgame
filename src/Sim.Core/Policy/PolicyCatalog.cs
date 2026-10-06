using System.Reflection;
using System.Text.Json;

namespace Sim.Core.Policy;

public sealed class PolicyDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Desc { get; set; } = "";
    public double Pc { get; set; }                 // political capital cost
    public int Delay { get; set; }                 // months from enactment to effect (legislation + implementation)
    public Dictionary<string, double> Mods { get; set; } = new();
    public Dictionary<string, double> Budget { get; set; } = new();   // BudgetLine name -> delta share of GDP
    public Dictionary<string, double> Subsidy { get; set; } = new();  // Sector name -> subsidy share of sector VA
    public double? Carbon { get; set; }
    public double OneOffRevenue { get; set; }      // share of GDP, booked as non-tax revenue (negative = purchase cost)
    public double ApprovalShock { get; set; }      // immediate one-off change to approval
    public string Group { get; set; } = "";        // mutually exclusive group
    public double MinDemocracy { get; set; }
    public double MaxDemocracy { get; set; } = 1;
}

public sealed class ProjectDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Asset { get; set; } = "";
    public double Cost { get; set; }               // share of potential GDP, total
    public int Months { get; set; }
    public double Bonus { get; set; }              // permanent asset-index boost on completion
    public double Pc { get; set; }
    public double Maint { get; set; }              // annual maintenance as share of build cost
    public string Desc { get; set; } = "";
}

public sealed class PolicyFile
{
    public List<PolicyDef> Policies { get; set; } = new();
    public List<ProjectDef> Projects { get; set; } = new();
}

public static class PolicyCatalog
{
    static PolicyFile? _file;
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static PolicyFile File => _file ??= Load();
    public static IReadOnlyList<PolicyDef> Policies => File.Policies;
    public static IReadOnlyList<ProjectDef> Projects => File.Projects;

    public static PolicyDef? Policy(string id) => File.Policies.FirstOrDefault(p => p.Id == id);
    public static ProjectDef? Project(string id) => File.Projects.FirstOrDefault(p => p.Id == id);

    static PolicyFile Load()
    {
        using var s = typeof(PolicyCatalog).Assembly.GetManifestResourceStream("data/policies.json")
            ?? throw new InvalidOperationException("Embedded data/policies.json not found");
        return JsonSerializer.Deserialize<PolicyFile>(s, Opts) ?? throw new InvalidDataException("bad policies.json");
    }
}
