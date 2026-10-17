using System.Diagnostics;
using System.Text.Json;
using Idle.Core;

public sealed record CareerCalibrationResult(int Seed, double? FirstPrestigeSeconds, double? BrasiliaUnlockedSeconds,
    double? FirstCampaignSeconds, double? LastCampaignSeconds, double? PresidencySeconds, int Campaigns, int Defeats,
    int[] CampaignsByOffice, double CampaignSeconds, double? EconomyWaitSeconds);

/// <summary>Active play from an untouched new game. No resources or timers are injected.</summary>
public static class CampaignCalibration
{
    private const double Step = .25;
    private const double Limit = 12 * 3600;

    public static CareerCalibrationResult Simulate(GameDefinition definition, int seed)
    {
        var random = new Random(seed);
        var engine = new GameEngine(definition, randomSample: random.NextDouble);
        var strategy = new ReinvestmentStrategy(engine);
        double? firstPrestige = null, brasiliaUnlocked = null, firstCampaign = null, lastCampaign = null, presidency = null;
        var campaigns = 0;
        var defeats = 0;
        var campaignsByOffice = new int[4];
        var campaignSeconds = 0d;
        string? lastResult = null;
        for (var tick = 0; tick < Limit / Step; tick++)
        {
            var time = tick * Step;
            for (var index = 0; index < 2; index++)
            {
                var id = definition.Worlds[index].Id;
                var state = engine.State.Worlds[id];
                if (state.Unlocked && state.PrestigeCount == 0 && engine.PreviewPrestige(id) > BigValue.Zero)
                {
                    engine.Prestige(id);
                    strategy.Invalidate(id);
                    if (index == 0) firstPrestige = time;
                    if (index == 1) brasiliaUnlocked = time;
                }
            }
            if (engine.State.Politics.ActiveCampaign is { PendingDecision: true })
                engine.ChooseCampaignOption(CampaignChoice.StrengthenBase);
            if (engine.State.Politics.ActiveCampaign is null && engine.StartCampaign(CampaignInvestment.Normal))
            {
                campaigns++;
                campaignsByOffice[engine.State.Politics.ActiveCampaign!.OfficeIndex - 1]++;
                firstCampaign ??= time;
                lastCampaign = time;
            }
            var preview = engine.PreviewCampaign(CampaignInvestment.Normal);
            var reserveBrasilia = engine.State.Politics.ActiveCampaign is null && preview.Office is not null &&
                engine.State.Politics.Support >= preview.SupportCost;
            foreach (var world in definition.Worlds)
            {
                if (!engine.State.Worlds[world.Id].Unlocked) continue;
                foreach (var producer in world.Producers) engine.StartCycle(world.Id, producer.Id);
                if (!(reserveBrasilia && world.Id == definition.Politics!.WorldId)) strategy.Invest(world);
            }
            if (engine.State.Politics.ActiveCampaign is not null) campaignSeconds += Step;
            engine.Advance(Step);
            var result = engine.State.Politics.LastResult;
            if (result is not null && result.Id != lastResult)
            {
                if (!result.Won) defeats++;
                lastResult = result.Id;
                if (!GameEngine.ValidateState(definition, engine.State, out var error))
                    throw new InvalidOperationException($"Seed {seed} produced a state that cannot be saved after an election: {error}");
            }
            if (engine.State.Politics.CurrentOffice == 4)
            {
                presidency = time + Step;
                break;
            }
        }
        return new(seed, firstPrestige, brasiliaUnlocked, firstCampaign, lastCampaign, presidency, campaigns, defeats,
            campaignsByOffice, campaignSeconds, presidency - brasiliaUnlocked - campaignSeconds);
    }

    public static void Report(GameDefinition definition, string[] args)
    {
        // Runtime-only overrides allow comparing politics parameters without touching economic data.
        definition = JsonSerializer.Deserialize<GameDefinition>(JsonSerializer.Serialize(definition))!;
        var factor = args.FirstOrDefault(arg => arg.StartsWith("--support-factor="));
        if (factor is not null) definition.Politics!.SupportEmissionFactor = double.Parse(factor.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture);
        var presidentCost = args.FirstOrDefault(arg => arg.StartsWith("--president-cost="));
        if (presidentCost is not null) definition.Politics!.Offices[^1].MoneyCost = BigValue.Parse(presidentCost.Split('=')[1]);
        var count = int.Parse(args.FirstOrDefault(arg => arg.StartsWith("--runs="))?.Split('=')[1] ?? "100");
        var output = Path.GetFullPath(args.FirstOrDefault(arg => arg.StartsWith("--output="))?["--output=".Length..]
            ?? Path.Combine("artifacts", "political-calibration.json"));
        var stopwatch = Stopwatch.StartNew();
        var runs = new List<CareerCalibrationResult>();
        for (var seed = 0; seed < count; seed++)
        {
            var result = Simulate(definition, seed);
            runs.Add(result);
            if (seed % 10 == 0 || seed == count - 1)
                Console.WriteLine($"Semente {seed}: Presidência em {result.PresidencySeconds / 3600:0.000} h; {result.Campaigns} campanhas; {result.Defeats} derrotas.");
        }
        var times = runs.Where(run => run.PresidencySeconds.HasValue).Select(run => run.PresidencySeconds!.Value).Order().ToArray();
        double MedianOf(IEnumerable<double> source)
        {
            var values = source.Order().ToArray();
            if (values.Length == 0) return 0;
            return values.Length % 2 == 0 ? (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2 : values[values.Length / 2];
        }
        double Percentile(double percentile) => times.Length == 0 ? double.NaN : times[Math.Clamp((int)Math.Ceiling(percentile * times.Length) - 1, 0, times.Length - 1)];
        var median = times.Length == 0 ? double.NaN : times.Length % 2 == 0 ? (times[times.Length / 2 - 1] + times[times.Length / 2]) / 2 : times[times.Length / 2];
        var report = new
        {
            Strategy = "New game, clicks every 0.25 s, automation as affordable; globally best marginal production/cost per world, waiting if needed; first prestige of Corre/Sindicato immediately; Normal campaigns and StrengthenBase at both decisions; reserve campaign money once support is sufficient; no offline gains or acceleration.",
            Seeds = count, Completed = times.Length, StepSeconds = Step, MaximumHours = Limit / 3600,
            MedianHours = median / 3600, P90Hours = Percentile(.9) / 3600,
            MinimumHours = times.Length == 0 ? 0 : times[0] / 3600, MaximumCompletedHours = times.Length == 0 ? 0 : times[^1] / 3600,
            FirstPrestigeMinutes = runs[0].FirstPrestigeSeconds / 60,
            BrasiliaUnlockMinutes = runs[0].BrasiliaUnlockedSeconds / 60,
            FirstCampaignMinutes = runs[0].FirstCampaignSeconds / 60,
            MedianEconomyWaitHours = MedianOf(runs.Where(run => run.EconomyWaitSeconds.HasValue).Select(run => run.EconomyWaitSeconds!.Value / 3600)),
            MedianLastCampaignHours = MedianOf(runs.Where(run => run.LastCampaignSeconds.HasValue).Select(run => run.LastCampaignSeconds!.Value / 3600)),
            TargetMedianHours = "2–4", MedianTargetPassed = median is >= 7200 and <= 14400,
            Politics = definition.Politics, WallSeconds = stopwatch.Elapsed.TotalSeconds, Runs = runs
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        var markdown = $"""
            # Calibração da carreira política

            - Partidas desde um save novo: **{count} sementes**, de 0 a {count - 1}; **{times.Length} concluídas** em até {Limit / 3600:0} h simuladas.
            - Estratégia: clique em todos os produtores ociosos a cada 0,25 s; compra de parceiros quando possível; reinvestimento na melhor produção marginal por custo, esperando saldo quando necessário.
            - Primeiro prestígio de Corre/Sindicato assim que disponível. Em Brasília, guardar verba da próxima campanha quando já há apoio suficiente; campanhas **Normal**, decisões **fortalecer a base**, nova tentativa imediata após derrota.
            - Sem recursos injetados, aceleração ou ganhos offline.
            - Primeiro prestígio de Corre: **{runs[0].FirstPrestigeSeconds / 60:0.00} min**. Brasília liberada: **{runs[0].BrasiliaUnlockedSeconds / 60:0.00} min**. Primeira campanha: **{runs[0].FirstCampaignSeconds / 60:0.00} min**.
            - Novo jogo → Presidência: mediana **{median / 3600:0.000} h**, P90 **{Percentile(.9) / 3600:0.000} h**; mínimo **{times.FirstOrDefault() / 3600:0.000} h**, máximo concluído **{times.LastOrDefault() / 3600:0.000} h**.
            - O JSON discrimina por semente os horários da última campanha, tentativas por cargo, tempo de campanha e espera por recursos após liberar Brasília.
            - Meta de mediana entre 2–4 h: **{(median is >= 7200 and <= 14400 ? "atingida" : "fora da faixa")}**. A meta do primeiro prestígio é 20–30 min.

            Mediana calculada pela média dos dois valores centrais; P90 pelo posto `ceil(0.90 × N)`. Os resultados individuais e parâmetros ficam no JSON ao lado.
            """;
        File.WriteAllText(Path.ChangeExtension(output, ".md"), markdown + Environment.NewLine);
        Console.WriteLine($"Calibração concluída: {times.Length}/{count}; mediana {median / 3600:0.000} h; P90 {Percentile(.9) / 3600:0.000} h; primeiro prestígio {runs[0].FirstPrestigeSeconds / 60:0.00} min. Relatório: {output}");
    }

    private sealed class ReinvestmentStrategy(GameEngine engine)
    {
        private sealed record Target(ProducerDefinition Producer, UpgradeDefinition? Upgrade, BigValue Cost);
        private readonly Dictionary<string, Target?> _targets = [];

        public void Invalidate(string world) => _targets.Remove(world);

        public void Invest(WorldDefinition world)
        {
            var state = engine.State.Worlds[world.Id];
            foreach (var producer in world.Producers)
            {
                var owned = state.Producers[producer.Id];
                if (owned.Quantity > 0 && !owned.Automated && state.Balance >= producer.AutomationCost)
                    engine.BuyAutomation(world.Id, producer.Id);
            }
            for (var purchase = 0; purchase < 100; purchase++)
            {
                if (!_targets.TryGetValue(world.Id, out var target)) _targets[world.Id] = target = FindTarget(world);
                if (target is null || state.Balance < target.Cost) return;
                var bought = target.Upgrade is null
                    ? engine.Buy(world.Id, target.Producer.Id, 1)
                    : engine.BuyUpgrade(world.Id, target.Producer.Id, target.Upgrade.Id);
                if (!bought) throw new InvalidOperationException("An affordable calibration purchase failed.");
                Invalidate(world.Id);
            }
        }

        private Target? FindTarget(WorldDefinition world)
        {
            var state = engine.State.Worlds[world.Id];
            var bestRatio = 0d;
            Target? best = null;
            foreach (var producer in world.Producers)
            {
                var owned = state.Producers[producer.Id];
                var previousRate = engine.GetCyclePayout(world.Id, producer.Id) / engine.GetCycleSeconds(world.Id, producer.Id);
                var price = engine.GetCost(world.Id, producer.Id, 1);
                owned.Quantity++;
                var nextRate = engine.GetCyclePayout(world.Id, producer.Id) / engine.GetCycleSeconds(world.Id, producer.Id);
                owned.Quantity--;
                var ratio = ((nextRate - previousRate) / price).ToDouble();
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    best = new(producer, null, price);
                }
                if (owned.Quantity == 0) continue;
                foreach (var upgrade in producer.Upgrades)
                {
                    if (owned.PurchasedUpgrades.Contains(upgrade.Id)) continue;
                    ratio = (previousRate * (upgrade.ProductionMultiplier * upgrade.SpeedMultiplier - 1) / upgrade.Cost).ToDouble();
                    if (ratio > bestRatio)
                    {
                        bestRatio = ratio;
                        best = new(producer, upgrade, upgrade.Cost);
                    }
                }
            }
            return best;
        }
    }
}
