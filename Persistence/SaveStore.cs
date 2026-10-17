using System.Text.Json;
using System.Text.Json.Serialization;
using Idle.Core;

namespace Idle.Persistence;

public sealed record LoadResult(GameEngine Engine, Dictionary<string, BigValue> OfflineEarnings, string? Notice);

/// <summary>Local snapshots. State and UTC timestamp are committed together.</summary>
public sealed class SaveStore
{
    public const int CurrentVersion = 1;
    private readonly string _directory;
    private readonly GameDefinition _definition;
    private bool _primaryIsValid;
    public string SavePath => Path.Combine(_directory, "save.json");
    public string BackupPath => Path.Combine(_directory, "save.backup.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 48
    };

    public SaveStore(string directory, GameDefinition definition)
    {
        _directory = Path.GetFullPath(directory);
        _definition = definition;
    }

    public LoadResult Load(DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        string? notice = null;
        var envelope = TryRead(SavePath, out var primaryError);
        _primaryIsValid = envelope != null;
        if (envelope == null && File.Exists(BackupPath))
        {
            envelope = TryRead(BackupPath, out _);
            if (envelope != null)
                notice = "O último backup válido foi recuperado. Parte do progresso mais recente pode ter sido perdida.";
        }

        if (envelope == null)
        {
            if (primaryError != null || File.Exists(BackupPath))
            {
                // Keep unknown, future-version or corrupt files for manual recovery.
                throw new InvalidDataException("Não foi possível abrir o save nem seu backup. Os arquivos foram preservados. " + primaryError);
            }
            var fresh = new GameEngine(_definition);
            Save(fresh, now);
            return new LoadResult(fresh, new Dictionary<string, BigValue>(), null);
        }

        var engine = new GameEngine(_definition, envelope.State);
        double seconds = Math.Clamp((now - envelope.SavedAtUtc).TotalSeconds, 0, _definition.OfflineLimitHours * 3600);
        if (envelope.SavedAtUtc > now)
            notice = Append(notice, "O relógio voltou no tempo; nenhum ganho offline foi aplicado.");
        var earnings = engine.Advance(seconds, offline: true);
        // Commit before exposing the awarded state: reopening cannot award this interval twice.
        Save(engine, now);
        return new LoadResult(engine, earnings, notice);
    }

    public void Save(GameEngine engine, DateTimeOffset now)
    {
        if (!GameEngine.ValidateState(_definition, engine.State, out var validationError))
            throw new InvalidDataException(validationError);
        Directory.CreateDirectory(_directory);
        string temporary = Path.Combine(_directory, "save.tmp.json");
        var envelope = new SaveEnvelope
        {
            Version = CurrentVersion,
            SavedAtUtc = now.ToUniversalTime(),
            State = engine.State
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, Json);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(SavePath))
            {
                // Never replace a valid backup with a corrupt primary during recovery.
                File.Replace(temporary, SavePath, _primaryIsValid ? BackupPath : null);
            }
            else
            {
                File.Move(temporary, SavePath);
            }
            _primaryIsValid = true;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private SaveEnvelope? TryRead(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
            return null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 4 * 1024 * 1024)
                throw new InvalidDataException("Arquivo de save maior que o limite de 4 MB.");
            var envelope = JsonSerializer.Deserialize<SaveEnvelope>(File.ReadAllBytes(path), Json)
                ?? throw new InvalidDataException("Save vazio.");
            if (envelope.Version > CurrentVersion)
                throw new NotSupportedException("Este save foi criado por uma versão mais recente do jogo. Os arquivos foram preservados.");
            if (envelope.Version != CurrentVersion)
                throw new InvalidDataException($"Versão de save não suportada: {envelope.Version}.");
            if (envelope.SavedAtUtc == default || envelope.State == null)
                throw new InvalidDataException("Snapshot incompleto.");
            if (!GameEngine.ValidateState(_definition, envelope.State, out var validationError))
                throw new InvalidDataException(validationError);
            return envelope;
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or InvalidOperationException or OverflowException)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string Append(string? first, string second) => first == null ? second : first + "\n" + second;

    private sealed class SaveEnvelope
    {
        public int Version { get; set; }
        public DateTimeOffset SavedAtUtc { get; set; }
        public GameState State { get; set; } = null!;
    }
}
