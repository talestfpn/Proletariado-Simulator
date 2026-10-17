using System.Text.Json;
using System.Text.Json.Nodes;
using Idle.Core;
using Idle.Persistence;

public static class PoliticalChecks
{
    private const string World = "world-3";
    private static void Check(bool condition, string description) => TestHarness.Check(condition, "Política: " + description);
    private static void Near(BigValue actual, BigValue expected, string description) => Check(TestHarness.Near(actual, expected), description);
    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 1e-9;

    public static GameEngine Ready(GameDefinition definition, double sample = .1)
    {
        var engine = new GameEngine(definition, randomSample: () => sample);
        for (var index = 0; index < 2; index++)
        {
            var world = definition.Worlds[index];
            engine.State.Worlds[world.Id].LifetimeEarnings = world.PrestigeScale;
            engine.Prestige(world.Id);
        }
        engine.State.Worlds[World].LifetimeEarnings = BigValue.Parse("1e24");
        engine.State.Worlds[World].Balance = BigValue.Parse("1e24");
        engine.SynchronizePoliticalSupport();
        return engine;
    }

    private static GameEngine Clone(GameEngine engine, double sample = .1) => new(engine.Definition,
        JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(engine.State))!, () => sample);

    public static void Run(GameDefinition definition)
    {
        Support(definition);
        Campaigns(definition);
        Boundaries(definition);
        SaveAndMigration(definition);
    }

    private static void Support(GameDefinition definition)
    {
        var locked = new GameEngine(definition);
        Check(!locked.PreviewCampaign(CampaignInvestment.Normal).CanStart && !locked.StartCampaign(CampaignInvestment.Normal),
            "Brasília bloqueada recusa campanha");
        var engine = Ready(definition);
        engine.State.Politics.Support = BigValue.Zero;
        engine.State.Politics.TotalIssued = BigValue.Zero;
        engine.State.Worlds[World].Balance = BigValue.Zero;
        engine.State.Worlds[World].LifetimeEarnings = definition.Worlds[2].PrestigeScale * 4;
        engine.Advance(0);
        var expected = BigValue.FromDouble(definition.Politics!.SupportEmissionFactor * 2).Floor();
        Near(engine.State.Politics.Support, expected, "emissão usa ganhos históricos pela raiz, mesmo com dinheiro gasto");
        engine.State.Politics.Support -= BigValue.FromDouble(10);
        engine.Advance(0);
        Near(engine.State.Politics.Support, expected - 10, "apoio gasto não reaparece sem novos ganhos");
        var snapshot = JsonSerializer.Serialize(engine.State.Politics);
        var other = JsonSerializer.Serialize(engine.State.Worlds["world-1"]);
        Check(engine.Prestige(World) && JsonSerializer.Serialize(engine.State.Politics) == snapshot,
            "prestígio de Brasília preserva apoio e emissão histórica");
        engine.Advance(0);
        Near(engine.State.Politics.Support, expected - 10, "prestígio não remite o mesmo apoio");
        Check(JsonSerializer.Serialize(engine.State.Worlds["world-1"]) == other && engine.State.Worlds[World].Unlocked,
            "prestígio preserva desbloqueio e o estado dos outros mundos");

        var producing = Ready(definition);
        producing.State.Worlds[World].Balance = producing.State.Worlds[World].LifetimeEarnings = BigValue.Zero;
        producing.State.Politics.Support = producing.State.Politics.TotalIssued = BigValue.Zero;
        var producer = producing.State.Worlds[World].Producers["producer-1"];
        producer.Quantity = 100;
        producing.StartCycle(World, "producer-1");
        var cycle = producing.GetCycleSeconds(World, "producer-1");
        producing.Advance(cycle);
        var afterManual = producing.State.Politics.Support;
        Check(afterManual > BigValue.Zero && afterManual == GameEngine.CalculateTotalPoliticalSupport(definition, producing.State),
            "produção manual em Brasília emite apoio mesmo com outro mundo selecionado");
        producer.Automated = producer.Running = true;
        producing.Advance(cycle * 10, offline: true);
        Check(producing.State.Politics.Support > afterManual &&
            producing.State.Politics.TotalIssued == GameEngine.CalculateTotalPoliticalSupport(definition, producing.State),
            "produção automatizada offline também emite somente apoio novo");
        var invalid = Ready(definition);
        invalid.State.Politics.Support = BigValue.FromDouble(1000.5);
        invalid.State.Politics.TotalIssued = BigValue.FromDouble(2000);
        Check(!GameEngine.ValidateState(definition, invalid.State), "save com apoio fracionário real é rejeitado");
    }

    private static void Campaigns(GameDefinition definition)
    {
        var engine = Ready(definition);
        var normal = engine.PreviewCampaign(CampaignInvestment.Normal);
        var reinforced = engine.PreviewCampaign(CampaignInvestment.Reinforced);
        var all = engine.PreviewCampaign(CampaignInvestment.AllIn);
        Check(normal.CanStart && reinforced.CanStart && all.CanStart && normal.Office!.Id == "vereador",
            "prévias iniciam no primeiro cargo e verificam dinheiro e apoio");
        Near(reinforced.MoneyCost, normal.MoneyCost * 2, "Reforçado cobra duas vezes a verba");
        Near(all.SupportCost, normal.SupportCost * 4, "Tudo no Palanque cobra quatro vezes o apoio");
        Check(Near(normal.WinChance, .55) && Near(reinforced.WinChance, .65) && Near(all.WinChance, .75),
            "chances básicas são 55%, 65% e 75%");
        var unfunded = Ready(definition);
        unfunded.State.Worlds[World].Balance = normal.MoneyCost - BigValue.One;
        Check(!unfunded.PreviewCampaign(CampaignInvestment.Normal).CanStart && !unfunded.StartCampaign(CampaignInvestment.Normal),
            "verba insuficiente recusa a campanha sem cobrança parcial");
        unfunded.State.Worlds[World].Balance = normal.MoneyCost * 10;
        unfunded.State.Politics.Support = normal.SupportCost - BigValue.One;
        var unfundedMoney = unfunded.State.Worlds[World].Balance;
        Check(!unfunded.StartCampaign(CampaignInvestment.Normal) && unfunded.State.Worlds[World].Balance == unfundedMoney &&
            unfunded.State.Politics.ActiveCampaign is null, "apoio insuficiente também recusa sem descontar dinheiro");
        engine.State.Worlds[World].Balance = normal.MoneyCost * 10;
        var money = engine.State.Worlds[World].Balance;
        var support = engine.State.Politics.Support;
        Check(engine.StartCampaign(CampaignInvestment.Normal), "abertura de campanha é aceita com saldo");
        Near(engine.State.Worlds[World].Balance, money - normal.MoneyCost, "investimento em dinheiro é cobrado integralmente na abertura");
        Near(engine.State.Politics.Support, support - normal.SupportCost, "apoio é cobrado integralmente na abertura");
        Check(!engine.StartCampaign(CampaignInvestment.AllIn) && !engine.Prestige(World), "uma campanha ativa bloqueia outra e o prestígio de Brasília");
        Check(!engine.ChooseCampaignOption(CampaignChoice.StrengthenBase), "não aceita escolha antes do evento");
        var duration = normal.DurationSeconds;
        engine.Advance(duration * 2);
        Check(engine.State.Politics.ActiveCampaign!.PendingDecision && Near(engine.State.Politics.ActiveCampaign.ElapsedSeconds, duration / 3),
            "intervalo longo para exatamente na primeira decisão e descarta o excesso político");
        Check(engine.ChooseCampaignOption(CampaignChoice.StrengthenBase) && !engine.ChooseCampaignOption(CampaignChoice.PersonalProjection),
            "primeira decisão é consumida uma vez");
        engine.Advance(duration / 3);
        Check(engine.State.Politics.ActiveCampaign!.PendingDecision && engine.ChooseCampaignOption(CampaignChoice.PersonalProjection),
            "segunda decisão abre em dois terços");
        Check(Near(engine.State.Politics.ActiveCampaign!.WinChance, .65) && Near(engine.State.Politics.ActiveCampaign.VictoryProductionBonus, .15),
            "escolhas calculam +10 pontos de chance e +5 pontos de bônus na vitória");
        engine.Advance(duration / 3);
        Check(engine.State.Politics.ActiveCampaign is null && engine.State.Politics.CurrentOffice == 1 && engine.State.Politics.LastResult!.Won,
            "votação concede o cargo uma vez");
        Check(Near(engine.State.Politics.GlobalProductionBonus, .15), "vitória concede bônus base e projeção pessoal");
        var bonus = engine.State.Politics.GlobalProductionBonus;
        engine.Advance(3600);
        Check(Near(engine.State.Politics.GlobalProductionBonus, bonus) && engine.AcknowledgeCampaignResult() && !engine.AcknowledgeCampaignResult(),
            "resultado e confirmação são idempotentes");
        Check(engine.PreviewCampaign(CampaignInvestment.Normal).Office!.Id == "deputado", "vitória libera exclusivamente o cargo seguinte");

        var failure = Ready(definition, .75);
        var initialQuantities = JsonSerializer.Serialize(failure.State.Worlds[World].Producers);
        failure.StartCampaign(CampaignInvestment.Normal);
        Resolve(failure, CampaignChoice.StrengthenBase, CampaignChoice.StrengthenBase);
        Check(failure.State.Politics.LastResult is { Won: false, Cancelled: false } && failure.State.Politics.CurrentOffice == 0 &&
            failure.State.Politics.GlobalProductionBonus == 0 && JsonSerializer.Serialize(failure.State.Worlds[World].Producers) == initialQuantities,
            "amostra exatamente na chance perde sem remover produtores ou conceder bônus");
        Check(failure.StartCampaign(CampaignInvestment.Normal), "derrota permite nova tentativa imediatamente");
        var moneyAfterOpening = failure.State.Worlds[World].Balance;
        var supportAfterOpening = failure.State.Politics.Support;
        Check(failure.CancelCampaign() && !failure.CancelCampaign() && failure.State.Politics.LastResult is { Cancelled: true, Won: false },
            "cancelamento remove campanha uma vez sem conceder cargo");
        Near(failure.State.Worlds[World].Balance, moneyAfterOpening, "cancelamento não devolve dinheiro");
        Near(failure.State.Politics.Support, supportAfterOpening, "cancelamento não devolve apoio");

        var cap = Ready(definition, .89999);
        cap.StartCampaign(CampaignInvestment.AllIn);
        Resolve(cap, CampaignChoice.StrengthenBase, CampaignChoice.StrengthenBase);
        Check(cap.State.Politics.LastResult!.Won, "chances com investimento máximo e base atingem o teto de 90%");
        var atCap = Ready(definition, .9);
        atCap.StartCampaign(CampaignInvestment.AllIn);
        Resolve(atCap, CampaignChoice.StrengthenBase, CampaignChoice.StrengthenBase);
        Check(!atCap.State.Politics.LastResult!.Won, "amostra no teto de 90% não vence por arredondamento");

        var career = Ready(definition);
        for (var i = 0; i < 4; i++)
        {
            Check(career.StartCampaign(CampaignInvestment.Normal), $"cargo {i + 1} começa na ordem correta");
            Resolve(career, CampaignChoice.StrengthenBase, CampaignChoice.StrengthenBase);
        }
        Check(career.State.Politics.CurrentOffice == 4 && Near(career.State.Politics.GlobalProductionBonus, .75) &&
            !career.PreviewCampaign(CampaignInvestment.Normal).CanStart && career.PreviewCampaign(CampaignInvestment.Normal).Office is null,
            "Presidência conclui o arco e acumula os quatro bônus por soma");
        var payout = new GameEngine(definition).GetCyclePayout("world-1", "producer-1");
        Near(career.GetCyclePayout("world-1", "producer-1"), payout * 1.25 * 1.75,
            "bônus global político multiplica prestígio sem somar diretamente aos pontos");
        var politicalSnapshot = JsonSerializer.Serialize(career.State.Politics);
        Check(career.Prestige(World) && JsonSerializer.Serialize(career.State.Politics) == politicalSnapshot,
            "pós-Presidência permite prestígio mantendo cargo e bônus");
        var finalValid = GameEngine.ValidateState(definition, career.State, out var validationError);
        Check(finalValid, "estado final da carreira é válido para save" + (finalValid ? "" : ": " + validationError));
    }

    public static void Resolve(GameEngine engine, CampaignChoice first, CampaignChoice second)
    {
        var duration = engine.State.Politics.ActiveCampaign!.DurationSeconds;
        engine.Advance(duration / 3);
        engine.ChooseCampaignOption(first);
        engine.Advance(duration / 3);
        engine.ChooseCampaignOption(second);
        engine.Advance(duration / 3);
    }

    private static void Boundaries(GameDefinition definition)
    {
        var whole = Ready(definition);
        var p = whole.State.Worlds["world-1"].Producers["producer-1"];
        p.Automated = p.Running = true;
        whole.StartCampaign(CampaignInvestment.Normal);
        var duration = whole.State.Politics.ActiveCampaign!.DurationSeconds;
        whole.Advance(duration / 3);
        whole.ChooseCampaignOption(CampaignChoice.StrengthenBase);
        whole.Advance(duration / 3);
        whole.ChooseCampaignOption(CampaignChoice.StrengthenBase);
        var split = Clone(whole);
        var payoutBefore = whole.GetCyclePayout("world-1", "producer-1");
        var remaining = duration / 3;
        var combined = whole.Advance(remaining * 2, offline: true)["world-1"];
        var sum = BigValue.Zero;
        for (var i = 0; i < remaining * 8; i++) sum += split.Advance(.25, offline: true)["world-1"];
        Near(combined, sum, "produção offline dividida equivale ao intervalo que atravessa a eleição");
        Near(combined, payoutBefore * remaining + payoutBefore * 1.1 * remaining,
            "vitória aplica o bônus no instante da eleição dentro do intervalo offline");
        Check(whole.State.Politics.CurrentOffice == split.State.Politics.CurrentOffice &&
            Near(whole.State.Politics.GlobalProductionBonus, split.State.Politics.GlobalProductionBonus),
            "fracionamento temporal mantém cargo e bônus");

        var paused = Ready(definition);
        paused.State.Worlds[World].Producers["producer-1"].Automated = true;
        paused.State.Worlds[World].Producers["producer-1"].Running = true;
        paused.StartCampaign(CampaignInvestment.Normal);
        var autoPayout = paused.GetCyclePayout(World, "producer-1");
        var seconds = paused.GetCycleSeconds(World, "producer-1");
        var gains = paused.Advance(48 * 3600, offline: true)[World];
        Near(gains, autoPayout * Math.Floor(8 * 3600 / seconds), "economia segue produzindo por oito horas durante decisão pendente");
        Check(paused.State.Politics.ActiveCampaign!.PendingDecision && Near(paused.State.Politics.ActiveCampaign.ElapsedSeconds, duration / 3),
            "campanha offline aguarda decisão sem consumir o tempo excedente");
    }

    private static void SaveAndMigration(GameDefinition definition)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Proletariado.PoliticalChecks"));
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var time = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var store = new SaveStore(directory, definition);
            var engine = Ready(definition, .99);
            engine.StartCampaign(CampaignInvestment.Normal);
            var duration = engine.State.Politics.ActiveCampaign!.DurationSeconds;
            engine.Advance(duration / 3);
            engine.ChooseCampaignOption(CampaignChoice.StrengthenBase);
            engine.Advance(duration / 3);
            engine.ChooseCampaignOption(CampaignChoice.StrengthenBase);
            store.Save(engine, time);
            var reopened = new SaveStore(directory, definition).Load(time);
            Check(reopened.Engine.State.Politics.ActiveCampaign!.RandomSample == .99,
                "reabertura preserva a amostra sorteada e as duas escolhas");
            var result = new SaveStore(directory, definition).Load(time.AddSeconds(duration)).Engine;
            Check(result.State.Politics.LastResult is { Won: false } && result.State.Politics.CurrentOffice == 0,
                "votação pode terminar offline com o resultado fixado na abertura");
            var sameResult = new SaveStore(directory, definition).Load(time.AddSeconds(duration)).Engine;
            Check(sameResult.State.Politics.LastResult!.Id == result.State.Politics.LastResult!.Id,
                "reabrir um resultado não resolve ou sorteia outra eleição");

            var winning = Ready(definition);
            winning.StartCampaign(CampaignInvestment.Normal);
            Resolve(winning, CampaignChoice.StrengthenBase, CampaignChoice.PersonalProjection);
            winning.State.Worlds["world-1"].Producers["producer-1"].Automated = true;
            winning.State.Worlds["world-1"].Producers["producer-1"].Running = true;
            store.Save(winning, time.AddHours(1));
            store.Save(winning, time.AddHours(1));
            File.WriteAllText(store.SavePath, "invalid primary");
            var recovered = new SaveStore(directory, definition).Load(time.AddHours(1));
            Check(recovered.Notice!.Contains("backup") && recovered.Engine.State.Politics.CurrentOffice == 1 &&
                Near(recovered.Engine.State.Politics.GlobalProductionBonus, .15), "backup conserva cargo e bônus sem repetir recompensa");
            new SaveStore(directory, definition).Load(time.AddMinutes(30));
            var afterClockCorrection = new SaveStore(directory, definition).Load(time.AddHours(1));
            Check(afterClockCorrection.OfflineEarnings.Values.All(value => value.IsZero),
                "relógio retrocedido não abre de novo o intervalo já consumido");

            var legacy = Ready(definition);
            legacy.State.Worlds[World].LifetimeEarnings = definition.Worlds[2].PrestigeScale * 4;
            legacy.State.Worlds[World].Balance = BigValue.Zero;
            var legacyState = JsonNode.Parse(JsonSerializer.Serialize(legacy.State))!;
            legacyState.AsObject().Remove("Politics");
            var envelope = new JsonObject { ["Version"] = 1, ["SavedAtUtc"] = JsonValue.Create(time.AddHours(2)), ["State"] = legacyState };
            File.WriteAllText(store.SavePath, envelope.ToJsonString());
            var migrated = new SaveStore(directory, definition).Load(time.AddHours(2)).Engine;
            var expected = BigValue.FromDouble(definition.Politics!.SupportEmissionFactor * 2).Floor();
            Near(migrated.State.Politics.Support, expected, "migração v1 credita apoio sobre todo o histórico de Brasília");
            Check(migrated.State.Politics.CurrentOffice == 0 && migrated.State.Politics.ActiveCampaign is null &&
                migrated.State.Politics.GlobalProductionBonus == 0 &&
                JsonNode.Parse(File.ReadAllText(store.SavePath))!["Version"]!.GetValue<int>() == 2,
                "migração v1→v2 inicia carreira sem cargo e grava formato novo");
            var migratedAgain = new SaveStore(directory, definition).Load(time.AddHours(2)).Engine;
            Near(migratedAgain.State.Politics.Support, expected, "migração e reabertura não duplicam apoio histórico");

            var completed = Ready(definition);
            for (var office = 0; office < 4; office++)
            {
                completed.StartCampaign(CampaignInvestment.Normal);
                Resolve(completed, CampaignChoice.StrengthenBase, CampaignChoice.StrengthenBase);
            }
            completed.Prestige(World);
            store.Save(completed, time.AddHours(3));
            var completedSave = new SaveStore(directory, definition).Load(time.AddHours(3)).Engine;
            Check(completedSave.State.Politics.CurrentOffice == 4 && Near(completedSave.State.Politics.GlobalProductionBonus, .75) &&
                GameEngine.ValidateState(definition, completedSave.State), "save e reabertura pós-Presidência e prestígio preservam inteiros de apoio");
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected test directory.");
            foreach (var file in Directory.EnumerateFiles(resolved)) File.Delete(file);
            Directory.Delete(resolved);
        }
    }
}
