namespace Idle.Core;

public sealed class GameState
{
    public string SelectedWorldId { get; set; } = "";
    public Dictionary<string, WorldState> Worlds { get; set; } = [];
}

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
