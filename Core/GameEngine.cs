namespace Idle.Core;

/// <summary>Scene-independent economy. The caller owns clocks, persistence and presentation.</summary>
public sealed partial class GameEngine
{
    private readonly Dictionary<string, WorldDefinition> _worlds;
    private readonly Dictionary<(string World, string Producer), ProducerDefinition> _producers;
    public GameDefinition Definition { get; }
    public GameState State { get; }

    public GameEngine(GameDefinition definition, GameState? state = null, Func<double>? randomSample = null)
    {
        ValidateDefinition(definition);
        Definition = definition;
        _worlds = definition.Worlds.ToDictionary(world => world.Id, StringComparer.Ordinal);
        _producers = definition.Worlds.SelectMany(world => world.Producers.Select(producer =>
            new KeyValuePair<(string, string), ProducerDefinition>((world.Id, producer.Id), producer))).ToDictionary();
        State = state ?? CreateInitialState(definition);
        _randomSample = randomSample ?? Random.Shared.NextDouble;
        if (!ValidateState(definition, State, out var error)) throw new ArgumentException(error, nameof(state));
    }

    public Dictionary<string, BigValue> Advance(double seconds, bool offline = false)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (offline) seconds = Math.Min(seconds, Definition.OfflineLimitHours * 3600);
        var gains = new Dictionary<string, BigValue>(StringComparer.Ordinal);
        var remaining = seconds;
        while (remaining > 0)
        {
            var campaign = State.Politics.ActiveCampaign;
            var campaignAdvances = campaign is not null && !campaign.PendingDecision;
            var boundary = campaignAdvances ? NextCampaignBoundary(campaign!) : double.PositiveInfinity;
            var interval = Math.Min(remaining, boundary);
            AccumulateGains(gains, AdvanceEconomy(interval, offline));
            remaining = Math.Max(0, remaining - interval);
            if (campaignAdvances)
            {
                campaign!.ElapsedSeconds = Math.Min(campaign.DurationSeconds, campaign.ElapsedSeconds + interval);
                if (campaign.ElapsedSeconds >= campaign.DurationSeconds - 1e-9 && campaign.Choices.Count == 2)
                    ResolveCampaign(campaign);
            }
            if (interval == 0 && ReferenceEquals(campaign, State.Politics.ActiveCampaign)) break;
        }
        // Keep zero-time advances useful after a migration or direct state restoration.
        SynchronizePoliticalSupport();
        foreach (var world in Definition.Worlds)
            if (State.Worlds[world.Id].Unlocked) gains.TryAdd(world.Id, BigValue.Zero);
        return gains;
    }

    private Dictionary<string, BigValue> AdvanceEconomy(double seconds, bool offline)
    {
        var gains = new Dictionary<string, BigValue>(StringComparer.Ordinal);
        foreach (var worldDefinition in Definition.Worlds)
        {
            var world = State.Worlds[worldDefinition.Id];
            if (!world.Unlocked) continue;
            var earned = BigValue.Zero;
            foreach (var definition in worldDefinition.Producers)
            {
                var producer = world.Producers[definition.Id];
                if (producer.Quantity == 0 || !producer.Running || (offline && !producer.Automated)) continue;
                var cycleSeconds = GetCycleSeconds(worldDefinition.Id, definition.Id);
                var elapsedCycles = seconds / cycleSeconds;
                if (!producer.Automated)
                {
                    var progress = producer.CycleProgress + elapsedCycles;
                    if (progress >= 1 - 1e-12)
                    {
                        earned += GetCyclePayout(worldDefinition.Id, definition.Id);
                        producer.Running = false;
                        producer.CycleProgress = 0;
                    }
                    else producer.CycleProgress = progress;
                    continue;
                }

                BigValue completed;
                if (double.IsFinite(elapsedCycles))
                {
                    var progress = producer.CycleProgress + elapsedCycles;
                    var rounded = Math.Round(progress);
                    // Stabilize exact cycle boundaries across ordinary frame-sized advances.
                    if (Math.Abs(progress - rounded) <= 1e-10 && rounded >= 1) progress = rounded;
                    var count = Math.Floor(progress);
                    completed = BigValue.FromDouble(count);
                    producer.CycleProgress = Math.Clamp(progress - count, 0, Math.BitDecrement(1d));
                }
                else
                {
                    // At this scale double has no fractional-cycle precision left to preserve.
                    completed = (BigValue.FromDouble(seconds) / cycleSeconds).Floor();
                    producer.CycleProgress = 0;
                }
                earned += GetCyclePayout(worldDefinition.Id, definition.Id) * completed;
            }
            world.Balance += earned;
            world.LifetimeEarnings += earned;
            gains[worldDefinition.Id] = earned;
        }
        SynchronizePoliticalSupport();
        return gains;
    }

    public bool StartCycle(string worldId, string producerId)
    {
        var world = GetWorldState(worldId);
        var producer = GetProducerState(worldId, producerId);
        if (!world.Unlocked || producer.Quantity <= 0 || producer.Running) return false;
        producer.Running = true;
        producer.CycleProgress = 0;
        return true;
    }

    public BigValue GetCost(string worldId, string producerId, int amount)
    {
        var definition = GetProducerDefinition(worldId, producerId);
        var producer = GetProducerState(worldId, producerId);
        if (amount < 0 || amount > int.MaxValue - producer.Quantity) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount == 0) return BigValue.Zero;
        var currentCost = definition.BaseCost * BigValue.Pow(definition.CostGrowth, producer.Quantity);
        if (amount == 1) return currentCost;
        if (definition.CostGrowth == 1) return currentCost * amount;
        // Geometric sum: the free initial unit counts toward the current price index.
        var sum = (BigValue.Pow(definition.CostGrowth, amount) - BigValue.One) / (definition.CostGrowth - 1);
        return currentCost * sum;
    }

    public int GetMaxAffordable(string worldId, string producerId)
    {
        var world = GetWorldState(worldId);
        var producer = GetProducerState(worldId, producerId);
        if (!world.Unlocked) return 0;
        var low = 0;
        var high = int.MaxValue - producer.Quantity;
        while (low < high)
        {
            var middle = low + (int)(((long)high - low + 1) / 2);
            if (GetCost(worldId, producerId, middle) <= world.Balance) low = middle;
            else high = middle - 1;
        }
        return low;
    }

    public bool Buy(string worldId, string producerId, int amount)
    {
        var world = GetWorldState(worldId);
        var producer = GetProducerState(worldId, producerId);
        if (!world.Unlocked || amount <= 0 || amount > int.MaxValue - producer.Quantity) return false;
        var cost = GetCost(worldId, producerId, amount);
        if (world.Balance < cost) return false;
        world.Balance -= cost;
        producer.Quantity += amount;
        return true;
    }

    public bool BuyAutomation(string worldId, string producerId)
    {
        var world = GetWorldState(worldId);
        var producer = GetProducerState(worldId, producerId);
        var definition = GetProducerDefinition(worldId, producerId);
        if (!world.Unlocked || producer.Quantity == 0 || producer.Automated || world.Balance < definition.AutomationCost) return false;
        world.Balance -= definition.AutomationCost;
        producer.Automated = true;
        producer.Running = true;
        return true;
    }

    public bool BuyUpgrade(string worldId, string producerId, string upgradeId)
    {
        var world = GetWorldState(worldId);
        var producer = GetProducerState(worldId, producerId);
        var definition = GetProducerDefinition(worldId, producerId);
        var upgrade = definition.Upgrades.FirstOrDefault(item => item.Id == upgradeId);
        if (upgrade is null || !world.Unlocked || producer.Quantity == 0 || producer.PurchasedUpgrades.Contains(upgradeId) || world.Balance < upgrade.Cost) return false;
        world.Balance -= upgrade.Cost;
        producer.PurchasedUpgrades.Add(upgradeId);
        return true;
    }

    public BigValue GetCyclePayout(string worldId, string producerId)
    {
        var definition = GetProducerDefinition(worldId, producerId);
        var producer = GetProducerState(worldId, producerId);
        var payout = definition.BasePayout * producer.Quantity * GetPrestigeMultiplier(worldId) * GetGlobalProductionMultiplier();
        foreach (var upgrade in definition.Upgrades)
            if (producer.PurchasedUpgrades.Contains(upgrade.Id)) payout *= upgrade.ProductionMultiplier;
        foreach (var milestone in definition.Milestones)
            if (producer.Quantity >= milestone.Quantity) payout *= milestone.ProductionMultiplier;
        return payout;
    }

    public double GetCycleSeconds(string worldId, string producerId)
    {
        var definition = GetProducerDefinition(worldId, producerId);
        var producer = GetProducerState(worldId, producerId);
        var speed = BigValue.One;
        foreach (var upgrade in definition.Upgrades)
            if (producer.PurchasedUpgrades.Contains(upgrade.Id)) speed *= upgrade.SpeedMultiplier;
        foreach (var milestone in definition.Milestones)
            if (producer.Quantity >= milestone.Quantity) speed *= milestone.SpeedMultiplier;
        return Math.Max(double.Epsilon, (BigValue.FromDouble(definition.CycleSeconds) / speed).ToDouble());
    }

    public BigValue GetProductionPerSecond(string worldId)
    {
        var world = GetWorldState(worldId);
        if (!world.Unlocked) return BigValue.Zero;
        var rate = BigValue.Zero;
        foreach (var producer in _worlds[worldId].Producers)
            if (world.Producers[producer.Id].Automated)
                rate += GetCyclePayout(worldId, producer.Id) / GetCycleSeconds(worldId, producer.Id);
        return rate;
    }

    public BigValue GetPrestigeMultiplier(string worldId) => BigValue.One + GetWorldState(worldId).PrestigePoints * Definition.PrestigeBonusPerPoint;

    public BigValue PreviewPrestige(string worldId)
    {
        var world = GetWorldState(worldId);
        if (!world.Unlocked) return BigValue.Zero;
        return (world.LifetimeEarnings / _worlds[worldId].PrestigeScale).Sqrt().Floor() - world.PrestigePoints;
    }

    public bool Prestige(string worldId)
    {
        var world = GetWorldState(worldId);
        if (Definition.Politics?.WorldId == worldId && State.Politics.ActiveCampaign is not null) return false;
        var reward = PreviewPrestige(worldId);
        if (reward.IsZero || world.PrestigeCount == int.MaxValue) return false;
        world.PrestigePoints += reward;
        world.PrestigeCount++;
        world.Balance = BigValue.Zero;
        world.Producers = CreateProducerStates(_worlds[worldId]);
        var index = Definition.Worlds.FindIndex(item => item.Id == worldId);
        if (index + 1 < Definition.Worlds.Count) State.Worlds[Definition.Worlds[index + 1].Id].Unlocked = true;
        return true;
    }

    public bool SelectWorld(string worldId)
    {
        if (!State.Worlds.TryGetValue(worldId, out var world) || !world.Unlocked) return false;
        State.SelectedWorldId = worldId;
        return true;
    }

    private WorldState GetWorldState(string worldId) => State.Worlds.TryGetValue(worldId, out var world)
        ? world : throw new ArgumentException("Unknown world ID.", nameof(worldId));

    private ProducerState GetProducerState(string worldId, string producerId)
    {
        var world = GetWorldState(worldId);
        return world.Producers.TryGetValue(producerId, out var producer)
            ? producer : throw new ArgumentException("Unknown producer ID.", nameof(producerId));
    }

    private ProducerDefinition GetProducerDefinition(string worldId, string producerId) => _producers.TryGetValue((worldId, producerId), out var producer)
        ? producer : throw new ArgumentException("Unknown world or producer ID.");

    private static GameState CreateInitialState(GameDefinition definition) => new()
    {
        SelectedWorldId = definition.Worlds[0].Id,
        Worlds = definition.Worlds.Select((world, index) => new KeyValuePair<string, WorldState>(world.Id,
            new WorldState { Unlocked = index == 0, Producers = CreateProducerStates(world) })).ToDictionary(StringComparer.Ordinal)
    };

    private static Dictionary<string, ProducerState> CreateProducerStates(WorldDefinition definition) =>
        definition.Producers.Select((producer, index) => new KeyValuePair<string, ProducerState>(producer.Id,
            new ProducerState { Quantity = index == 0 ? 1 : 0 })).ToDictionary(StringComparer.Ordinal);

    public static bool ValidateState(GameDefinition definition, GameState state) => ValidateState(definition, state, out _);

    public static bool ValidateState(GameDefinition definition, GameState state, out string error)
    {
        error = "";
        try { ValidateDefinition(definition); }
        catch (ArgumentException ex) { error = ex.Message; return false; }
        if (state is null || state.Worlds is null || state.Worlds.Count != definition.Worlds.Count)
        { error = "The save must contain every defined world exactly once."; return false; }
        if (state.SelectedWorldId is null || !state.Worlds.TryGetValue(state.SelectedWorldId, out var selected) || selected is null || !selected.Unlocked)
        { error = "The selected world must exist and be unlocked."; return false; }
        for (var index = 0; index < definition.Worlds.Count; index++)
        {
            var worldDefinition = definition.Worlds[index];
            if (!state.Worlds.TryGetValue(worldDefinition.Id, out var world) || world is null || world.Producers is null || world.Producers.Count != worldDefinition.Producers.Count)
            { error = "The save contains an unknown world or producer set."; return false; }
            if (world.PrestigeCount < 0 || world.Balance > world.LifetimeEarnings || world.PrestigePoints != world.PrestigePoints.Floor())
            { error = "Invalid world earnings or prestige state."; return false; }
            var expectedUnlocked = index == 0 || state.Worlds.TryGetValue(definition.Worlds[index - 1].Id, out var previous) && previous is not null && previous.PrestigeCount > 0;
            if (world.Unlocked != expectedUnlocked || !world.Unlocked && world.PrestigeCount > 0)
            { error = "Invalid world unlock sequence."; return false; }
            foreach (var producerDefinition in worldDefinition.Producers)
            {
                if (!world.Producers.TryGetValue(producerDefinition.Id, out var producer) || producer is null || producer.Quantity < 0 ||
                    !double.IsFinite(producer.CycleProgress) || producer.CycleProgress < 0 || producer.CycleProgress >= 1 ||
                    producer.PurchasedUpgrades is null || producer.PurchasedUpgrades.Any(id => !producerDefinition.Upgrades.Any(upgrade => upgrade.Id == id)) ||
                    producer.Quantity == 0 && (producer.Running || producer.Automated || producer.PurchasedUpgrades.Count > 0) ||
                    !producer.Running && producer.CycleProgress != 0 || producer.Automated && !producer.Running)
                { error = "Invalid producer quantity, cycle or upgrade state."; return false; }
            }
        }
        return ValidatePoliticsState(definition, state, out error);
    }

    public static void ValidateDefinition(GameDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!double.IsFinite(definition.OfflineLimitHours) || definition.OfflineLimitHours < 0 || definition.OfflineLimitHours > double.MaxValue / 3600 ||
            !double.IsFinite(definition.PrestigeBonusPerPoint) || definition.PrestigeBonusPerPoint < 0 || definition.Worlds is null || definition.Worlds.Count == 0)
            throw new ArgumentException("Invalid game definition.", nameof(definition));
        var worldIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var world in definition.Worlds)
        {
            if (world is null || !IsValidId(world.Id) || !worldIds.Add(world.Id) || world.PrestigeScale.IsZero || world.Producers is null || world.Producers.Count == 0)
                throw new ArgumentException("Invalid or duplicate world definition.", nameof(definition));
            var producerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var producer in world.Producers)
            {
                if (producer is null || !IsValidId(producer.Id) || !producerIds.Add(producer.Id) || producer.BaseCost.IsZero || producer.BasePayout.IsZero ||
                    !double.IsFinite(producer.CostGrowth) || producer.CostGrowth < 1 || !double.IsFinite(producer.CycleSeconds) || producer.CycleSeconds <= 0 ||
                    producer.Upgrades is null || producer.Milestones is null)
                    throw new ArgumentException("Invalid or duplicate producer definition.", nameof(definition));
                var upgradeIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var upgrade in producer.Upgrades)
                    if (upgrade is null || !IsValidId(upgrade.Id) || !upgradeIds.Add(upgrade.Id) || !IsMultiplier(upgrade.ProductionMultiplier) || !IsMultiplier(upgrade.SpeedMultiplier))
                        throw new ArgumentException("Invalid or duplicate upgrade definition.", nameof(definition));
                var quantities = new HashSet<int>();
                foreach (var milestone in producer.Milestones)
                    if (milestone is null || milestone.Quantity <= 0 || !quantities.Add(milestone.Quantity) || !IsMultiplier(milestone.ProductionMultiplier) || !IsMultiplier(milestone.SpeedMultiplier))
                        throw new ArgumentException("Invalid or duplicate milestone definition.", nameof(definition));
            }
        }
        ValidatePoliticsDefinition(definition);
    }

    private static bool IsMultiplier(double value) => double.IsFinite(value) && value >= 1;
    private static bool IsValidId(string? id) => !string.IsNullOrEmpty(id) && id.All(character =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.');
}
