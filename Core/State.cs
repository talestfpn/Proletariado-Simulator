namespace Idle.Core;

public sealed class GameState
{
    public string SelectedWorldId { get; set; } = "";
    public Dictionary<string, WorldState> Worlds { get; set; } = [];
    public PoliticsState Politics { get; set; } = new();
}

public enum CampaignInvestment { Normal, Reinforced, AllIn }
public enum CampaignChoice { StrengthenBase, PersonalProjection }

public sealed class PoliticsState
{
    public BigValue Support { get; set; }
    public BigValue TotalIssued { get; set; }
    /// <summary>Zero before a victory; one through four for the sequential offices.</summary>
    public int CurrentOffice { get; set; }
    public double GlobalProductionBonus { get; set; }
    public CampaignState? ActiveCampaign { get; set; }
    public CampaignResult? LastResult { get; set; }
}

public sealed class CampaignState
{
    public int OfficeIndex { get; set; }
    public CampaignInvestment Investment { get; set; }
    public double ElapsedSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public double BaseWinChance { get; set; }
    public double BaseProductionBonus { get; set; }
    public BigValue InvestedMoney { get; set; }
    public BigValue InvestedSupport { get; set; }
    public double RandomSample { get; set; }
    public List<CampaignChoice> Choices { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public bool PendingDecision => Choices.Count < 2 && ElapsedSeconds >= DurationSeconds * (Choices.Count + 1) / 3 - 1e-9;
    [System.Text.Json.Serialization.JsonIgnore]
    public double WinChance => Math.Min(.9, Math.Round(BaseWinChance + Choices.Count(choice => choice == CampaignChoice.StrengthenBase) * .1, 10));
    [System.Text.Json.Serialization.JsonIgnore]
    public double VictoryProductionBonus => BaseProductionBonus + Choices.Count(choice => choice == CampaignChoice.PersonalProjection) * .05;
}

public sealed class CampaignResult
{
    public string Id { get; set; } = "";
    public int OfficeIndex { get; set; }
    public bool Won { get; set; }
    public bool Cancelled { get; set; }
    public double ProductionBonusGranted { get; set; }
    public bool Acknowledged { get; set; }
}

public sealed record CampaignPreview(OfficeDefinition? Office, BigValue MoneyCost, BigValue SupportCost,
    double WinChance, double DurationSeconds, bool CanStart, string? BlockReason);

public sealed class WorldState
{
    public bool Unlocked { get; set; }
    public BigValue Balance { get; set; }
    public BigValue LifetimeEarnings { get; set; }
    public BigValue PrestigePoints { get; set; }
    public int PrestigeCount { get; set; }
    public Dictionary<string, ProducerState> Producers { get; set; } = [];
}

public sealed class ProducerState
{
    public int Quantity { get; set; }
    public bool Automated { get; set; }
    public bool Running { get; set; }
    public double CycleProgress { get; set; }
    public HashSet<string> PurchasedUpgrades { get; set; } = [];
}
