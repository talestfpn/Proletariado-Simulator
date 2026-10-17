using System.Globalization;
using System.Text.Json;
using Idle.Core;
using Idle.Persistence;

public static class PersistenceChecks
{
    public static void Run(GameDefinition definition, Action<bool, string> check)
    {
        var time = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        using (var directory = new TestDirectory())
        {
            var store = new SaveStore(directory.Path, definition);
            var loaded = store.Load(time);
            var engine = loaded.Engine;
            string worldId = definition.Worlds[0].Id;
            string producerId = definition.Worlds[0].Producers[0].Id;
            var producer = engine.State.Worlds[worldId].Producers[producerId];
            producer.Automated = true;
            producer.Running = true;
            producer.Quantity = 10;
            store.Save(engine, time);
            var expected = engine.Advance(8 * 3600, offline: true)[worldId];
            var returned = store.Load(time.AddDays(2));
            check(TestHarness.Near(returned.OfflineEarnings[worldId], expected), "Save: limite offline de 8 horas");
            BigValue balance = returned.Engine.State.Worlds[worldId].Balance;
            var reopened = store.Load(time.AddDays(2));
            check(reopened.Engine.State.Worlds[worldId].Balance == balance, "Save: reabrir não duplica ganhos offline");
            check(File.Exists(store.BackupPath), "Save: mantém backup do snapshot anterior");
            using var document = JsonDocument.Parse(File.ReadAllText(store.SavePath));
            check(document.RootElement.GetProperty("Version").GetInt32() == SaveStore.CurrentVersion,
                "Save: formato versionado");
        }

        using (var directory = new TestDirectory())
        {
            var store = new SaveStore(directory.Path, definition);
            var engine = store.Load(time).Engine;
            string worldId = definition.Worlds[0].Id;
            string producerId = definition.Worlds[0].Producers[0].Id;
            engine.StartCycle(worldId, producerId);
            engine.Advance(definition.Worlds[0].Producers[0].CycleSeconds * .3);
            double fraction = engine.State.Worlds[worldId].Producers[producerId].CycleProgress;
            store.Save(engine, time.AddHours(1));
            var returned = store.Load(time);
            check(returned.Engine.State.Worlds[worldId].Balance == BigValue.Zero &&
                returned.Engine.State.Worlds[worldId].Producers[producerId].CycleProgress == fraction,
                "Save: relógio retrocedido não dá ganhos nem avança ciclo manual");
            check(returned.Notice != null, "Save: informa ajuste de relógio");
        }

        using (var directory = new TestDirectory())
        {
            var store = new SaveStore(directory.Path, definition);
            var engine = store.Load(time).Engine;
            string worldId = definition.Worlds[0].Id;
            string producerId = definition.Worlds[0].Producers[0].Id;
            engine.State.Worlds[worldId].Producers[producerId].Automated = true;
            engine.State.Worlds[worldId].Producers[producerId].Running = true;
            store.Save(engine, time);
            store.Save(engine, time);
            File.WriteAllText(store.SavePath, "{broken");
            var recovered = store.Load(time.AddSeconds(10));
            check(recovered.Notice?.Contains("backup", StringComparison.OrdinalIgnoreCase) == true &&
                recovered.Engine.State.Worlds[worldId].Balance > BigValue.Zero,
                "Save: arquivo corrompido recupera backup e produção");
            var repeat = store.Load(time.AddSeconds(10));
            check(repeat.Engine.State.Worlds[worldId].Balance == recovered.Engine.State.Worlds[worldId].Balance,
                "Save: recuperação também persiste ganhos uma única vez");
        }

        using (var directory = new TestDirectory())
        {
            var store = new SaveStore(directory.Path, definition);
            var engine = store.Load(time).Engine;
            store.Save(engine, time);
            File.WriteAllText(store.SavePath, "primary invalid");
            File.WriteAllText(store.BackupPath, "backup invalid");
            bool rejected = false;
            try { store.Load(time); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && File.ReadAllText(store.SavePath) == "primary invalid" &&
                File.ReadAllText(store.BackupPath) == "backup invalid", "Save: preserva arquivos quando ambos são inválidos");
        }

        using (var directory = new TestDirectory())
        {
            var store = new SaveStore(directory.Path, definition);
            var engine = store.Load(time).Engine;
            store.Save(engine, time);
            string futureSave = File.ReadAllText(store.SavePath).Replace($"\"Version\": {SaveStore.CurrentVersion}", $"\"Version\": {SaveStore.CurrentVersion + 1}", StringComparison.Ordinal);
            File.WriteAllText(store.SavePath, futureSave);
            bool rejected = false;
            try { store.Load(time); }
            catch (NotSupportedException) { rejected = true; }
            check(rejected && File.ReadAllText(store.SavePath) == futureSave,
                "Save: versão futura é preservada sem regressão automática ao backup");
        }

        using (var directory = new TestDirectory())
        {
            var store = new SaveStore(directory.Path, definition);
            var engine = store.Load(time).Engine;
            string worldId = definition.Worlds[0].Id;
            engine.State.Worlds[worldId].LifetimeEarnings = definition.Worlds[0].PrestigeScale * 4;
            engine.Prestige(worldId);
            store.Save(engine, time);
            var changed = JsonSerializer.Deserialize<GameDefinition>(JsonSerializer.Serialize(definition))!;
            changed.Worlds[0].PrestigeScale *= 100;
            var rebalanced = new SaveStore(directory.Path, changed).Load(time).Engine;
            check(rebalanced.State.Worlds[worldId].PrestigePoints == BigValue.FromDouble(2) &&
                rebalanced.PreviewPrestige(worldId).IsZero && !rebalanced.Prestige(worldId),
                "Save: rebalancear escala preserva bônus já concedido sem repetir recompensa");
            rebalanced.State.Worlds[worldId].LifetimeEarnings = changed.Worlds[0].PrestigeScale * 9;
            check(rebalanced.PreviewPrestige(worldId) == BigValue.One,
                "Save: nova escala concede apenas a diferença dos pontos já recebidos");
        }

        using (var directory = new TestDirectory())
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
                var store = new SaveStore(directory.Path, definition);
                var engine = store.Load(time).Engine;
                var world = engine.State.Worlds[definition.Worlds[0].Id];
                world.Balance = BigValue.Parse("1.23456e400");
                world.LifetimeEarnings = world.Balance;
                store.Save(engine, time);
                var reopened = store.Load(time);
                check(TestHarness.Near(reopened.Engine.State.Worlds[definition.Worlds[0].Id].Balance, world.Balance),
                    "Save: valores acima de 1e308 e cultura pt-BR preservados");
            }
            finally { CultureInfo.CurrentCulture = original; }
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        private static readonly string Root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IdleLab.Tests"));
        public string Path { get; } = System.IO.Path.Combine(Root, Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            string resolved = System.IO.Path.GetFullPath(Path);
            if (!resolved.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Diretório de teste fora da raiz esperada.");
            foreach (string file in Directory.EnumerateFiles(resolved))
                File.Delete(file);
            Directory.Delete(resolved);
        }
    }
}
