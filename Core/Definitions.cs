namespace Idle.Core;

public sealed class GameDefinition
{
    public double OfflineLimitHours { get; set; } = 8;
    public double PrestigeBonusPerPoint { get; set; } = .25;
    public List<WorldDefinition> Worlds { get; set; } = [];
}

public sealed class WorldDefinition
{
    public string Id { get; set; } = "";
    public BigValue PrestigeScale { get; set; }
    public List<ProducerDefinition> Producers { get; set; } = [];
}

public sealed class ProducerDefinition
{
    public string Id { get; set; } = "";
    public BigValue BaseCost { get; set; }
    public double CostGrowth { get; set; }
    public BigValue BasePayout { get; set; }
    public double CycleSeconds { get; set; }
    public BigValue AutomationCost { get; set; }
    public List<UpgradeDefinition> Upgrades { get; set; } = [];
    public List<MilestoneDefinition> Milestones { get; set; } = [];
}

public sealed class UpgradeDefinition
{
    public string Id { get; set; } = "";
    public BigValue Cost { get; set; }
    public double ProductionMultiplier { get; set; } = 1;
    public double SpeedMultiplier { get; set; } = 1;
}

public sealed class MilestoneDefinition
{
    public int Quantity { get; set; }
    public double ProductionMultiplier { get; set; } = 2;
    public double SpeedMultiplier { get; set; } = 1;
}
