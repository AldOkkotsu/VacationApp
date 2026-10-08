using System.Text.Json;
using System.Text.Json.Serialization;

namespace VacationApp.Core;

/// <summary>
/// Storage for the demonstration. Register one VacationService instance per store;
/// the service serializes all reads and mutations in that process.
/// </summary>
public interface IVacationRepository
{
    VacationSnapshot? Load();
    void Save(VacationSnapshot snapshot);
}

public sealed class MemoryVacationRepository(VacationSnapshot? initialSnapshot = null) : IVacationRepository
{
    private VacationSnapshot? _snapshot = initialSnapshot;

    public VacationSnapshot? Load() => _snapshot;

    public void Save(VacationSnapshot snapshot) => _snapshot = snapshot;
}

/// <summary>
/// Stores demo data locally. This is neither a shared database nor credential storage.
/// A failed write leaves the preceding file intact. Use one service instance per file.
/// </summary>
public sealed class JsonVacationRepository : IVacationRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public JsonVacationRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public VacationSnapshot? Load()
    {
        if (!File.Exists(_path))
            return null;

        using var stream = File.OpenRead(_path);
        return JsonSerializer.Deserialize<VacationSnapshot>(stream, Options)
            ?? throw new InvalidDataException("El archivo de vacaciones está vacío o dañado.");
    }

    public void Save(VacationSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, snapshot, Options);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
