using Idle.Core;

public sealed record CalibrationResult(double? FirstPrestigeSeconds, BigValue EarningsAt25Minutes, BigValue TotalEarnings, int[] Quantities);

/// <summary>Deterministic active play, without injected resources or modified production time.</summary>
public static class Calibration
{
    private const string W = "world-1";
    private const double Step = .25;

    public static CalibrationResult Simulate(GameDefinition definition, double seconds)
    {
        var engine = new GameEngine(definition);
        var world = engine.State.Worlds[W];
        var producers = definition.Worlds[0].Producers;
        double? firstPrestige = null;
        var earnings25 = BigValue.Zero;
        for (var tick = 0; tick < seconds / Step; tick++)
        {
            foreach (var producer in producers) engine.StartCycle(W, producer.Id);
            engine.Advance(Step);
            var time = (tick + 1) * Step;
            if (firstPrestige is null && engine.PreviewPrestige(W) > BigValue.Zero) firstPrestige = time;
            if (Math.Abs(time - 1500) < Step / 2) earnings25 = world.LifetimeEarnings;
            Invest(engine, producers);
        }
        return new(firstPrestige, earnings25, world.LifetimeEarnings, producers.Select(p => world.Producers[p.Id].Quantity).ToArray());
    }

    private static void Invest(GameEngine engine, List<ProducerDefinition> producers)
    {
        var world = engine.State.Worlds[W];
        // Automation is purchased in producer order as soon as its owner can pay for it.
        foreach (var producer in producers)
        {
            var state = world.Producers[producer.Id];
            if (state.Quantity > 0 && !state.Automated && world.Balance >= producer.AutomationCost)
                engine.BuyAutomation(W, producer.Id);
        }

        // Wait for the globally best marginal return; do not drain funds on worse affordable buys.
        for (var purchase = 0; purchase < 100; purchase++)
        {
            double bestRatio = 0;
            ProducerDefinition? bestProducer = null;
            UpgradeDefinition? bestUpgrade = null;
            foreach (var producer in producers)
            {
                var state = world.Producers[producer.Id];
                var previousRate = engine.GetCyclePayout(W, producer.Id) / engine.GetCycleSeconds(W, producer.Id);
                var price = engine.GetCost(W, producer.Id, 1);
                state.Quantity++;
                var nextRate = engine.GetCyclePayout(W, producer.Id) / engine.GetCycleSeconds(W, producer.Id);
                state.Quantity--;
                var ratio = ((nextRate - previousRate) / price).ToDouble();
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    bestProducer = producer;
                    bestUpgrade = null;
                }
                if (state.Quantity == 0) continue;
                foreach (var upgrade in producer.Upgrades)
                {
                    if (state.PurchasedUpgrades.Contains(upgrade.Id)) continue;
                    var gain = previousRate * (upgrade.ProductionMultiplier * upgrade.SpeedMultiplier - 1);
                    ratio = (gain / upgrade.Cost).ToDouble();
                    if (ratio > bestRatio)
                    {
                        bestRatio = ratio;
                        bestProducer = producer;
                        bestUpgrade = upgrade;
                    }
                }
            }
            if (bestProducer is null) break;
            var bought = bestUpgrade is null
                ? engine.Buy(W, bestProducer.Id, 1)
                : engine.BuyUpgrade(W, bestProducer.Id, bestUpgrade.Id);
            if (!bought) break;
        }
    }

    public static void Report(GameDefinition definition)
    {
        var result = Simulate(definition, 1800);
        Console.WriteLine("Calibração: cliques de produtores ociosos a cada 0,25 s; automações compradas quando disponíveis; unidade ou melhoria com melhor produção marginal/custo, aguardando saldo se necessário.");
        Console.WriteLine($"Ganhos brutos em 25 min: {result.EarningsAt25Minutes}");
        Console.WriteLine($"Primeiro prestígio com escala atual: {result.FirstPrestigeSeconds / 60:0.000} min");
        Console.WriteLine($"Ganhos brutos em 30 min: {result.TotalEarnings}");
        Console.WriteLine($"Quantidades aos 30 min: {string.Join(", ", result.Quantities)}");
    }
}
