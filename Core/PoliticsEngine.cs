namespace Idle.Core;

public sealed partial class GameEngine
{
    private readonly Func<double> _randomSample;

    public BigValue GetGlobalProductionMultiplier() => BigValue.FromDouble(1 + State.Politics.GlobalProductionBonus);

    public CampaignPreview PreviewCampaign(CampaignInvestment investment)
    {
        if (!Enum.IsDefined(investment)) throw new ArgumentOutOfRangeException(nameof(investment));
        var politics = Definition.Politics;
        var state = State.Politics;
        if (politics is null)
            return new(null, BigValue.Zero, BigValue.Zero, 0, 0, false, "A carreira política não está disponível.");
        if (state.CurrentOffice >= politics.Offices.Count)
            return new(null, BigValue.Zero, BigValue.Zero, 0, 0, false, "A Presidência já foi conquistada.");
        var office = politics.Offices[state.CurrentOffice];
        var (multiplier, chance) = GetInvestment(investment);
        var money = office.MoneyCost * multiplier;
        var support = office.SupportCost * multiplier;
        string? reason = null;
        if (!State.Worlds[politics.WorldId].Unlocked) reason = "Libere Brasília para iniciar sua carreira política.";
        else if (state.ActiveCampaign is not null) reason = "Já existe uma campanha em andamento.";
        else if (State.Worlds[politics.WorldId].Balance < money) reason = "Falta verba de Brasília para este investimento.";
        else if (state.Support < support) reason = "Falta apoio político para este investimento.";
        return new(office, money, support, chance, office.DurationSeconds, reason is null, reason);
    }

    public bool StartCampaign(CampaignInvestment investment)
    {
        SynchronizePoliticalSupport();
        var preview = PreviewCampaign(investment);
        if (!preview.CanStart) return false;
        var sample = _randomSample();
        if (!double.IsFinite(sample) || sample < 0 || sample >= 1)
            throw new InvalidOperationException("Campaign random samples must be in [0, 1).");
        State.Worlds[Definition.Politics!.WorldId].Balance -= preview.MoneyCost;
        State.Politics.Support = SubtractWholeAmounts(State.Politics.Support, preview.SupportCost);
        State.Politics.ActiveCampaign = new CampaignState
        {
            OfficeIndex = State.Politics.CurrentOffice + 1,
            Investment = investment,
            DurationSeconds = preview.DurationSeconds,
            BaseWinChance = preview.WinChance,
            BaseProductionBonus = preview.Office!.ProductionBonus,
            InvestedMoney = preview.MoneyCost,
            InvestedSupport = preview.SupportCost,
            RandomSample = sample
        };
        return true;
    }

    public bool ChooseCampaignOption(CampaignChoice choice)
    {
        if (!Enum.IsDefined(choice)) throw new ArgumentOutOfRangeException(nameof(choice));
        var campaign = State.Politics.ActiveCampaign;
        if (campaign is null || !campaign.PendingDecision) return false;
        campaign.Choices.Add(choice);
        return true;
    }

    public bool CancelCampaign()
    {
        var campaign = State.Politics.ActiveCampaign;
        if (campaign is null) return false;
        State.Politics.LastResult = new CampaignResult
        {
            Id = Guid.NewGuid().ToString("N"), OfficeIndex = campaign.OfficeIndex, Cancelled = true
        };
        State.Politics.ActiveCampaign = null;
        return true;
    }

    public bool AcknowledgeCampaignResult()
    {
        var result = State.Politics.LastResult;
        if (result is null || result.Acknowledged) return false;
        result.Acknowledged = true;
        return true;
    }

    /// <summary>Issues only the support not already credited against historical gross earnings.</summary>
    public void SynchronizePoliticalSupport()
    {
        var politics = Definition.Politics;
        if (politics is null || !State.Worlds[politics.WorldId].Unlocked) return;
        var emitible = CalculateTotalPoliticalSupport(Definition, State);
        var earned = SubtractWholeAmounts(emitible, State.Politics.TotalIssued);
        if (earned.IsZero) return;
        State.Politics.Support = AddWholeAmounts(State.Politics.Support, earned);
        State.Politics.TotalIssued = emitible;
    }

    public static BigValue CalculateTotalPoliticalSupport(GameDefinition definition, GameState state)
    {
        var politics = definition.Politics;
        if (politics is null || !state.Worlds.TryGetValue(politics.WorldId, out var world) || !world.Unlocked)
            return BigValue.Zero;
        var scale = definition.Worlds.First(item => item.Id == politics.WorldId).PrestigeScale;
        return ((world.LifetimeEarnings / scale).Sqrt() * politics.SupportEmissionFactor).Floor();
    }

    /// <summary>Version 1 has no political spending history, so all historical support is credited.</summary>
    public static void MigrateVersion1State(GameDefinition definition, GameState state)
    {
        state.Politics = new PoliticsState();
        if (!ValidateState(definition, state, out var error)) throw new ArgumentException(error, nameof(state));
        var issued = CalculateTotalPoliticalSupport(definition, state);
        state.Politics = new PoliticsState { Support = issued, TotalIssued = issued };
    }

    private static (double Multiplier, double Chance) GetInvestment(CampaignInvestment investment) => investment switch
    {
        CampaignInvestment.Normal => (1, .55),
        CampaignInvestment.Reinforced => (2, .65),
        CampaignInvestment.AllIn => (4, .75),
        _ => throw new ArgumentOutOfRangeException(nameof(investment))
    };

    private static double NextCampaignBoundary(CampaignState campaign)
    {
        var target = campaign.Choices.Count < 2
            ? campaign.DurationSeconds * (campaign.Choices.Count + 1) / 3
            : campaign.DurationSeconds;
        return Math.Max(0, target - campaign.ElapsedSeconds);
    }

    private void ResolveCampaign(CampaignState campaign)
    {
        // Removing the active campaign and applying its reward are one snapshot mutation.
        // Subsequent advances and restored snapshots cannot resolve the same campaign twice.
        if (!ReferenceEquals(campaign, State.Politics.ActiveCampaign)) return;
        var won = campaign.RandomSample < campaign.WinChance;
        var bonus = won ? campaign.VictoryProductionBonus : 0;
        if (won)
        {
            State.Politics.CurrentOffice = campaign.OfficeIndex;
            State.Politics.GlobalProductionBonus += bonus;
        }
        State.Politics.LastResult = new CampaignResult
        {
            Id = Guid.NewGuid().ToString("N"), OfficeIndex = campaign.OfficeIndex,
            Won = won, ProductionBonusGranted = bonus
        };
        State.Politics.ActiveCampaign = null;
    }

    private static void AccumulateGains(Dictionary<string, BigValue> total, Dictionary<string, BigValue> interval)
    {
        foreach (var pair in interval) total[pair.Key] = total.GetValueOrDefault(pair.Key) + pair.Value;
    }

    // Support is integral, while BigValue uses a binary floating-point mantissa.
    // For precisely representable integer ranges, operate in decimal to avoid
    // accumulating fractional dust each time a campaign spends support.
    private static BigValue AddWholeAmounts(BigValue left, BigValue right) => left.Exponent < 15 && right.Exponent < 15
        ? BigValue.FromDouble((double)(ReadWholeDecimal(left) + ReadWholeDecimal(right))) : left + right;

    private static BigValue SubtractWholeAmounts(BigValue left, BigValue right) => left <= right ? BigValue.Zero :
        left.Exponent < 15 && right.Exponent < 15
            ? BigValue.FromDouble((double)Math.Max(0, ReadWholeDecimal(left) - ReadWholeDecimal(right))) : left - right;

    private static decimal ReadWholeDecimal(BigValue value)
    {
        if (value.IsZero) return 0;
        var amount = decimal.Parse(value.Mantissa.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0L; index < value.Exponent; index++) amount *= 10;
        return decimal.Round(amount, 0, MidpointRounding.ToEven);
    }

    private static bool IsWholeAmount(BigValue value)
    {
        if (value.IsZero || value.Exponent >= 16) return true;
        if (value.Exponent < 0) return false;
        var scale = Math.Pow(10, value.Exponent);
        var amount = decimal.Parse(value.Mantissa.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0L; index < value.Exponent; index++) amount *= 10;
        // Only representation-sized residue is accepted, never an arbitrary
        // percentage tolerance that could admit meaningful fractional support.
        var oneUlp = (Math.BitIncrement(value.Mantissa) - value.Mantissa) * scale;
        var tolerance = (decimal)Math.Max(1e-14, oneUlp * 2);
        return Math.Abs(amount - decimal.Round(amount, 0, MidpointRounding.ToEven)) <= tolerance;
    }

    private static void ValidatePoliticsDefinition(GameDefinition definition)
    {
        var politics = definition.Politics;
        if (politics is null) return;
        if (!definition.Worlds.Any(world => world.Id == politics.WorldId) ||
            !double.IsFinite(politics.SupportEmissionFactor) || politics.SupportEmissionFactor <= 0 ||
            politics.Offices is null || politics.Offices.Count != 4)
            throw new ArgumentException("Invalid politics definition.", nameof(definition));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var office in politics.Offices)
            if (office is null || !IsValidId(office.Id) || !ids.Add(office.Id) || string.IsNullOrWhiteSpace(office.Name) ||
                !double.IsFinite(office.DurationSeconds) || office.DurationSeconds <= 0 || office.DurationSeconds > double.MaxValue / 3 || office.MoneyCost.IsZero ||
                office.SupportCost.IsZero || !IsWholeAmount(office.SupportCost) ||
                !double.IsFinite(office.ProductionBonus) || office.ProductionBonus < 0 || office.ProductionBonus > 10)
                throw new ArgumentException("Invalid or duplicate office definition.", nameof(definition));
    }

    private static bool ValidatePoliticsState(GameDefinition definition, GameState game, out string error)
    {
        error = "";
        var state = game.Politics;
        if (state is null || state.Support > state.TotalIssued || !IsWholeAmount(state.Support) ||
            !IsWholeAmount(state.TotalIssued) || !double.IsFinite(state.GlobalProductionBonus) ||
            state.GlobalProductionBonus < 0)
        { error = "Invalid political support or bonus state."; return false; }
        var politics = definition.Politics;
        if (politics is null)
        {
            if (!state.Support.IsZero || !state.TotalIssued.IsZero || state.CurrentOffice != 0 ||
                state.GlobalProductionBonus != 0 || state.ActiveCampaign is not null || state.LastResult is not null)
            { error = "Politics is disabled by this definition."; return false; }
            return true;
        }
        if (state.CurrentOffice < 0 || state.CurrentOffice > politics.Offices.Count)
        { error = "Invalid political office."; return false; }
        var minimumBonus = politics.Offices.Take(state.CurrentOffice).Sum(office => office.ProductionBonus);
        if (state.GlobalProductionBonus < minimumBonus - 1e-9 ||
            state.GlobalProductionBonus > minimumBonus + state.CurrentOffice * .1 + 1e-9 ||
            !game.Worlds[politics.WorldId].Unlocked && (!state.TotalIssued.IsZero || state.CurrentOffice > 0 || state.ActiveCampaign is not null))
        { error = "Invalid political career progression."; return false; }
        var campaign = state.ActiveCampaign;
        if (campaign is not null)
        {
            if (campaign.OfficeIndex != state.CurrentOffice + 1 || campaign.OfficeIndex > politics.Offices.Count ||
                !Enum.IsDefined(campaign.Investment) || campaign.Choices is null || campaign.Choices.Count > 2 ||
                campaign.Choices.Any(choice => !Enum.IsDefined(choice)) ||
                !double.IsFinite(campaign.DurationSeconds) || campaign.DurationSeconds <= 0 ||
                !double.IsFinite(campaign.ElapsedSeconds) || campaign.ElapsedSeconds < 0 || campaign.ElapsedSeconds >= campaign.DurationSeconds ||
                !double.IsFinite(campaign.RandomSample) || campaign.RandomSample < 0 || campaign.RandomSample >= 1 ||
                !double.IsFinite(campaign.BaseWinChance) || campaign.BaseWinChance != GetInvestment(campaign.Investment).Chance ||
                !double.IsFinite(campaign.BaseProductionBonus) || campaign.BaseProductionBonus < 0 || campaign.BaseProductionBonus > 10 ||
                campaign.InvestedMoney.IsZero || campaign.InvestedSupport.IsZero || !IsWholeAmount(campaign.InvestedSupport) ||
                campaign.Choices.Count > 0 && campaign.ElapsedSeconds < campaign.DurationSeconds * campaign.Choices.Count / 3 - 1e-9 ||
                campaign.Choices.Count < 2 && campaign.ElapsedSeconds > campaign.DurationSeconds * (campaign.Choices.Count + 1) / 3 + 1e-9)
            { error = "Invalid active campaign snapshot."; return false; }
        }
        var result = state.LastResult;
        if (result is not null && (!Guid.TryParseExact(result.Id, "N", out _) || result.OfficeIndex < 1 || result.OfficeIndex > politics.Offices.Count ||
            result.Won && result.Cancelled || !double.IsFinite(result.ProductionBonusGranted) || result.ProductionBonusGranted < 0 ||
            !result.Won && result.ProductionBonusGranted != 0 || result.Won && result.OfficeIndex > state.CurrentOffice))
        { error = "Invalid election result snapshot."; return false; }
        return true;
    }
}
