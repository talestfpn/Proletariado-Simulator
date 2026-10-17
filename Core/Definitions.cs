namespace Idle.Core;

public sealed class GameDefinition
{
    public double OfflineLimitHours { get; set; } = 8;
    public double PrestigeBonusPerPoint { get; set; } = .25;
    public List<WorldDefinition> Worlds { get; set; } = [];
    public PoliticsDefinition? Politics { get; set; }
}

public sealed class PoliticsDefinition
{
    public string WorldId { get; set; } = "world-3";
    public double SupportEmissionFactor { get; set; } = 1000;
    public List<OfficeDefinition> Offices { get; set; } = [];
}

public sealed class OfficeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double DurationSeconds { get; set; }
    public BigValue MoneyCost { get; set; }
    public BigValue SupportCost { get; set; }
    public double ProductionBonus { get; set; }
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
