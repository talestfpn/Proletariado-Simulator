using System.Text.Json;
using System.Text.Json.Nodes;
using Idle.Core;

public static class TestHarness
{
    public static int Failures { get; private set; }
    public static int Checks { get; private set; }

    public static void Check(bool condition, string message)
    {
        Checks++;
        if (!condition) Failures++;
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {message}");
    }

    public static bool Near(BigValue actual, BigValue expected, double tolerance = 1e-9)
    {
        if (expected.IsZero || actual.IsZero) return expected.IsZero && actual.IsZero;
        var ratio = (actual / expected).ToDouble();
        return Math.Abs(ratio - 1) <= tolerance;
    }
}

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string W = "world-1";
    private const string P = "producer-1";

    public static int Main(string[] args)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<GameDefinition>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "balance.json")), JsonOptions)!;
            if (args.Contains("--calibrate"))
            {
                Calibration.Report(definition);
                return 0;
            }
            Check(definition.Worlds.Count == 3 && definition.Worlds.All(w => w.Producers.Count == 8), "três mundos com oito produtores nos dados");
            Check(definition.OfflineLimitHours == 8 && definition.PrestigeBonusPerPoint == .25, "limite offline e bônus de prestígio configuráveis");
            Numbers();
            Purchases(definition);
            Production(definition);
            Prestige(definition);
            Retheme(definition);
            PersistenceChecks.Run(definition, TestHarness.Check);
            var run = Calibration.Simulate(definition, 1800);
            Check(run.FirstPrestigeSeconds is >= 1200 and <= 1800, $"primeiro prestígio da estratégia de reinvestimento em 20–30 min ({run.FirstPrestigeSeconds / 60:0.00} min)");
            Console.WriteLine($"{TestHarness.Checks - TestHarness.Failures}/{TestHarness.Checks} verificações aprovadas.");
            return TestHarness.Failures == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Check(bool condition, string name) => TestHarness.Check(condition, name);
    private static void Near(BigValue actual, BigValue expected, string name) => Check(TestHarness.Near(actual, expected), name);

    private static GameEngine Fresh(GameDefinition definition) => new(definition);
    private static void Fund(GameEngine engine, string world = W)
    {
        engine.State.Worlds[world].Balance = BigValue.Parse("1e12");
        engine.State.Worlds[world].LifetimeEarnings = BigValue.Parse("1e12");
    }

    private static GameEngine Clone(GameEngine engine) => new(engine.Definition,
        JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(engine.State))!);

    private static void Numbers()
    {
        var huge = BigValue.Parse("1e400");
        Near(huge + BigValue.Parse("1e399"), BigValue.Parse("1.1e400"), "adição além do limite de double");
        Near(huge * huge, BigValue.Parse("1e800"), "multiplicação de valores grandes");
        Near(BigValue.Parse("1e800").Sqrt(), huge, "raiz quadrada de valores grandes");
        Near(BigValue.FromDouble(4.9).Floor(), BigValue.FromDouble(4), "piso para concessão de pontos inteiros");
        Near(BigValue.Parse("1.13e2").Floor(), BigValue.FromDouble(113), "piso preserva inteiro em notação científica");
        Near(BigValue.FromDouble(12769).Sqrt().Floor(), BigValue.FromDouble(113), "raiz de quadrado perfeito não perde ponto por arredondamento");
        Check(BigValue.Pow(1.12, 100000).Exponent > 308, "potência de preço mantém expoente grande");
        Near(JsonSerializer.Deserialize<BigValue>(JsonSerializer.Serialize(huge)), huge, "JSON preserva mantissa e expoente");
        Check(!string.IsNullOrWhiteSpace(huge.Format()) && huge.Format().Contains('e'), "valores grandes têm representação legível");
        try { JsonSerializer.Deserialize<BigValue>("123"); Check(false, "JSON exige valores monetários como string"); }
        catch (JsonException) { Check(true, "JSON exige valores monetários como string"); }
    }

    private static void Purchases(GameDefinition definition)
    {
        var batch = Fresh(definition);
        batch.State.Worlds[W].Balance = BigValue.FromDouble(10000);
        batch.State.Worlds[W].LifetimeEarnings = batch.State.Worlds[W].Balance;
        var singles = Clone(batch);
        var quoted = batch.GetCost(W, P, 10);
        var sum = BigValue.Zero;
        for (var i = 0; i < 10; i++)
        {
            sum += singles.GetCost(W, P, 1);
            singles.Buy(W, P, 1);
        }
        Check(batch.Buy(W, P, 10), "compra de lote 10 é aceita");
        Near(quoted, sum, "custo de lote equivale à soma de dez compras individuais");
        Check(batch.State.Worlds[W].Producers[P].Quantity == singles.State.Worlds[W].Producers[P].Quantity, "lote e compras individuais entregam a mesma quantidade");
        Near(batch.State.Worlds[W].Balance, singles.State.Worlds[W].Balance, "lote e compras individuais descontam o mesmo saldo");

        var max = Fresh(definition);
        max.State.Worlds[W].Balance = BigValue.FromDouble(12345);
        max.State.Worlds[W].LifetimeEarnings = max.State.Worlds[W].Balance;
        var count = max.GetMaxAffordable(W, P);
        Check(count > 0 && max.GetCost(W, P, count) <= max.State.Worlds[W].Balance
            && max.GetCost(W, P, count + 1) > max.State.Worlds[W].Balance, "Máximo encontra o maior lote pagável");
        var previous = max.State.Worlds[W].Balance;
        var total = max.GetCost(W, P, count);
        Check(max.Buy(W, P, count) && max.State.Worlds[W].Balance >= BigValue.Zero, "Máximo compra sem saldo negativo");
        Near(max.State.Worlds[W].Balance, previous - total, "Máximo desconta exatamente o lote");
        Check(max.GetMaxAffordable(W, P) == 0 && !max.Buy(W, P, 1), "nenhuma unidade extra cabe após Máximo");
        Check(!max.Buy("world-2", P, 1), "mundo bloqueado recusa compras");
        var blocked = Fresh(definition);
        blocked.State.Worlds[W].Balance = blocked.GetCost(W, P, 9);
        blocked.State.Worlds[W].LifetimeEarnings = blocked.State.Worlds[W].Balance;
        var snapshot = JsonSerializer.Serialize(blocked.State);
        Check(!blocked.Buy(W, P, 10) && snapshot == JsonSerializer.Serialize(blocked.State), "lote sem saldo é recusado inteiro, sem compra parcial");
    }

    private static void Production(GameDefinition definition)
    {
        var manual = Fresh(definition);
        var payout = manual.GetCyclePayout(W, P);
        var duration = manual.GetCycleSeconds(W, P);
        Check(manual.StartCycle(W, P) && !manual.StartCycle(W, P), "clique inicia apenas um ciclo manual");
        manual.Advance(duration * .5);
        Near(manual.State.Worlds[W].Balance, BigValue.Zero, "ciclo incompleto ainda não entrega rendimento");
        manual.Advance(duration * .5);
        Near(manual.State.Worlds[W].Balance, payout, "conclusão manual entrega um pagamento");
        manual.Advance(duration * 100);
        Near(manual.State.Worlds[W].Balance, payout, "produtor manual aguarda novo clique");

        var auto = Fresh(definition);
        Fund(auto);
        Check(auto.BuyAutomation(W, P), "automação pode ser comprada");
        var earned = auto.Advance(duration * 10.25)[W];
        Near(earned, payout * 10, "automação repete dez ciclos completos");
        Check(Math.Abs(auto.State.Worlds[W].Producers[P].CycleProgress - .25) < 1e-9, "automação preserva fração remanescente");

        var whole = Fresh(definition);
        Fund(whole);
        whole.Buy(W, "producer-2", 7);
        whole.BuyAutomation(W, P);
        whole.BuyAutomation(W, "producer-2");
        whole.State.Worlds[W].Balance = BigValue.Zero;
        whole.State.Worlds[W].LifetimeEarnings = BigValue.Zero;
        var split = Clone(whole);
        whole.Advance(1234.567);
        for (var i = 0; i < 4938; i++) split.Advance(.25);
        split.Advance(.067);
        Near(whole.State.Worlds[W].LifetimeEarnings, split.State.Worlds[W].LifetimeEarnings, "produção independe da subdivisão temporal");
        foreach (var id in new[] { P, "producer-2" })
            Check(Math.Abs(whole.State.Worlds[W].Producers[id].CycleProgress - split.State.Worlds[W].Producers[id].CycleProgress) < 1e-8, $"fração de ciclo é estável por intervalo: {id}");

        var speed = Fresh(definition);
        Fund(speed);
        speed.StartCycle(W, P);
        speed.Advance(duration * .35);
        var fraction = speed.State.Worlds[W].Producers[P].CycleProgress;
        Check(speed.BuyUpgrade(W, P, "speed-1"), "melhoria de velocidade é comprada");
        Check(Math.Abs(speed.State.Worlds[W].Producers[P].CycleProgress - fraction) < 1e-12, "melhoria de velocidade preserva proporção concluída");
        Check(Math.Abs(speed.GetCycleSeconds(W, P) - duration / 2) < 1e-9, "melhoria reduz duração conforme multiplicador");
        Near(speed.Advance(duration * .325)[W], payout, "ciclo acelerado termina no tempo remanescente proporcional");
        Check(speed.BuyUpgrade(W, P, "output-1") && !speed.BuyUpgrade(W, P, "output-1"), "melhoria é concedida uma única vez");
        Near(speed.GetCyclePayout(W, P), payout * 2, "melhoria de rendimento multiplica produção");

        var milestones = Fresh(definition);
        milestones.State.Worlds[W].Producers[P].Quantity = 24;
        var prior = milestones.GetCyclePayout(W, P);
        milestones.State.Worlds[W].Producers[P].Quantity = 25;
        Near(milestones.GetCyclePayout(W, P), prior * (25d / 24) * 2, "marco de 25 unidades aplica bônus além da nova unidade");

        var offline = Fresh(definition);
        offline.StartCycle(W, P);
        Near(offline.Advance(3600, true)[W], BigValue.Zero, "produção offline ignora ciclo manual");
        Check(offline.State.Worlds[W].Producers[P].CycleProgress == 0, "ciclo manual fica congelado offline");
        Fund(offline);
        offline.BuyAutomation(W, P);
        Near(offline.Advance(3600 * 24, true)[W], payout * (3600 * 8 / duration), "produção offline respeita teto de oito horas");
    }

    private static void Prestige(GameDefinition definition)
    {
        var engine = Fresh(definition);
        var world = engine.State.Worlds[W];
        Check(!engine.Prestige(W), "reinício sem recompensa é recusado");
        world.LifetimeEarnings = definition.Worlds[0].PrestigeScale * 4;
        world.Balance = BigValue.Zero;
        Near(engine.PreviewPrestige(W), BigValue.FromDouble(2), "prestígio usa ganhos brutos mesmo com saldo gasto");
        Check(engine.Prestige(W) && engine.State.Worlds["world-2"].Unlocked, "primeiro prestígio desbloqueia o próximo mundo");
        Near(world.PrestigePoints, BigValue.FromDouble(2), "pontos são concedidos pela raiz dos ganhos acumulados");
        Near(engine.GetPrestigeMultiplier(W), BigValue.FromDouble(1.5), "cada ponto adiciona 25% ao multiplicador");
        Check(!engine.Prestige(W), "repetir prestígio sem novos ganhos não concede pontos");
        Check(world.Producers[P].Quantity == 1 && world.Producers.Values.Skip(1).All(p => p.Quantity == 0)
            && world.Balance.IsZero && world.Producers.Values.All(p => !p.Automated && !p.Running && p.PurchasedUpgrades.Count == 0), "reinício limpa a run e restaura uma unidade gratuita");
        Near(world.LifetimeEarnings, definition.Worlds[0].PrestigeScale * 4, "reinício preserva ganhos acumulados");
        var baseline = Fresh(definition);
        Check(engine.GetCyclePayout(W, P) > baseline.GetCyclePayout(W, P), "run seguinte recupera produção mais rápido graças ao bônus");
        engine.StartCycle(W, P);
        baseline.StartCycle(W, P);
        Near(engine.Advance(1)[W], baseline.Advance(1)[W] * 1.5, "mesmo primeiro ciclo da nova run entrega 50% a mais com dois pontos");

        Fund(engine);
        engine.BuyAutomation(W, P);
        engine.SelectWorld("world-2");
        world.Balance = BigValue.Zero;
        world.LifetimeEarnings = definition.Worlds[0].PrestigeScale * 4;
        var gain = engine.Advance(10)[W];
        Check(engine.State.SelectedWorldId == "world-2" && gain > BigValue.Zero && world.Balance > BigValue.Zero, "mundo não selecionado continua produzindo e recebe saldo");

        var other = engine.State.Worlds["world-2"];
        other.Balance = BigValue.FromDouble(123);
        other.LifetimeEarnings = BigValue.FromDouble(456);
        other.Producers[P].Quantity = 3;
        var snapshot = JsonSerializer.Serialize(other);
        world.LifetimeEarnings = definition.Worlds[0].PrestigeScale * 9;
        Check(engine.Prestige(W), "novos ganhos liberam outro prestígio");
        Check(snapshot == JsonSerializer.Serialize(other), "prestígio mantém os demais mundos intactos");
        other.LifetimeEarnings = definition.Worlds[1].PrestigeScale;
        Check(engine.Prestige("world-2") && engine.State.Worlds["world-3"].Unlocked, "primeiro prestígio do segundo mundo libera o terceiro");
    }

    private static void Retheme(GameDefinition definition)
    {
        var themePath = Path.Combine(AppContext.BaseDirectory, "Data", "theme.json");
        var theme = JsonNode.Parse(File.ReadAllText(themePath))!;
        var engine = Fresh(definition);
        Fund(engine);
        engine.Buy(W, P, 10);
        var originalSave = JsonSerializer.Serialize(engine.State);
        var firstWorld = theme["worlds"]![0]!;
        firstWorld["name"] = "Tema alternativo";
        firstWorld["accentColor"] = "#FF00FF";
        firstWorld["glyph"] = "[]";
        firstWorld["producers"]![0]!["name"] = "Nome diferente";
        var restored = new GameEngine(definition, JsonSerializer.Deserialize<GameState>(originalSave)!);
        Check(firstWorld["id"]!.GetValue<string>() == W && firstWorld["producers"]![0]!["id"]!.GetValue<string>() == P,
            "troca de nome, cor e ícone mantém IDs da apresentação");
        Check(JsonSerializer.Serialize(restored.State) == originalSave, "mesmo save continua válido após trocar o tema");
        Near(restored.GetCyclePayout(W, P), engine.GetCyclePayout(W, P), "tema não interfere nos cálculos");
    }
}
