using System.Text.Json;

namespace Ams2.Shared.Victory;

/// <summary>
/// Config global do tema da vitoria (nao e por perfil de tema visual): ligado, volume 0..100 e o arquivo de audio de cada tema.
/// Os audios sao escolhidos pelo usuario e copiados para <c>{raiz}\victory\</c>; nada e embutido (direitos autorais).
/// </summary>
public sealed record VictoryConfig
{
    public bool Enabled { get; init; }
    public int VolumePct { get; init; } = 70;
    public string DefaultPath { get; init; } = "";
    public string SennaPath { get; init; } = "";
    public string BarrichelloPath { get; init; } = "";
    public string MassaPath { get; init; } = "";

    public string PathFor(VictoryTheme t) => t switch
    {
        VictoryTheme.Senna => SennaPath,
        VictoryTheme.Barrichello => BarrichelloPath,
        VictoryTheme.Massa => MassaPath,
        _ => DefaultPath,
    };

    public VictoryConfig WithPath(VictoryTheme t, string path) => t switch
    {
        VictoryTheme.Senna => this with { SennaPath = path },
        VictoryTheme.Barrichello => this with { BarrichelloPath = path },
        VictoryTheme.Massa => this with { MassaPath = path },
        _ => this with { DefaultPath = path },
    };

    public VictoryConfig Normalized() => this with
    {
        VolumePct = Math.Clamp(VolumePct, 0, 100),
        DefaultPath = DefaultPath ?? "", SennaPath = SennaPath ?? "", BarrichelloPath = BarrichelloPath ?? "", MassaPath = MassaPath ?? "",
    };

    /// <summary>Arquivo a tocar: o do tema; se faltar (vazio ou inexistente), o do Padrao; se tambem faltar, null (silencio).</summary>
    public string? Resolve(VictoryTheme theme, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var p = PathFor(theme);
        if (!string.IsNullOrWhiteSpace(p) && exists(p)) return p;
        p = DefaultPath;
        return !string.IsNullOrWhiteSpace(p) && exists(p) ? p : null;
    }
}

/// <summary>
/// <c>{raiz}\victory.json</c> (raiz padrao %AppData%\ams2-live-coach, a mesma do ProfileStore) e a pasta <c>{raiz}\victory\</c> com as copias dos audios.
/// Escrita atomica; <see cref="Refresh"/> relê o arquivo se mudou por fora (Control Center com o host fechado/aberto).
/// <see cref="InMemory"/> nao grava nada.
/// </summary>
public sealed class VictoryStore
{
    public const string FileName = "victory.json";
    public const string FolderName = "victory";
    public static readonly string[] AudioExtensions = [".mp3", ".wav", ".wma", ".m4a"];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly string? _root;
    readonly object _gate = new();
    volatile VictoryConfig _current = new();
    DateTime _stamp = DateTime.MinValue;
    long _length = -1;

    public VictoryStore(string? root = null)
    {
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ams2-live-coach");
        Refresh();
    }

    VictoryStore() { }
    public static VictoryStore InMemory() => new();

    public VictoryConfig Current => _current;
    string? FilePath => _root is null ? null : Path.Combine(_root, FileName);
    public string? Folder => _root is null ? null : Path.Combine(_root, FolderName);

    public void Save(VictoryConfig config)
    {
        config = config.Normalized();
        lock (_gate)
        {
            _current = config;
            if (FilePath is not { } path) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(config, Json));
                File.Move(tmp, path, overwrite: true);
                Stamp(out _stamp, out _length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Relê o arquivo se mudou. Devolve true se a config em memoria mudou.</summary>
    public bool Refresh()
    {
        if (FilePath is not { } path) return false;
        lock (_gate)
        {
            if (!Stamp(out var t, out var len)) return false;
            if (t == _stamp && len == _length) return false;
            _stamp = t; _length = len;
            try
            {
                var m = JsonSerializer.Deserialize<VictoryConfig>(File.ReadAllText(path), Json);
                if (m is null) return false;
                m = m.Normalized();
                bool changed = m != _current;
                _current = m;
                return changed;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>Copia o audio escolhido para a pasta do store como <c>{tema}{ext}</c> (apaga a copia anterior do tema) e devolve o caminho novo.</summary>
    public string ImportFile(VictoryTheme theme, string sourcePath)
    {
        var folder = Folder ?? throw new InvalidOperationException("Sem pasta de configuracao.");
        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AudioExtensions.Contains(ext)) throw new InvalidOperationException($"Formato nao suportado: {ext} (use mp3, wav, wma ou m4a).");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Arquivo nao encontrado.", sourcePath);
        Directory.CreateDirectory(folder);
        string dest = Path.Combine(folder, theme.ToString().ToLowerInvariant() + ext);
        // O usuario pode escolher a propria copia (mesmo arquivo): nao apagar antes de copiar.
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            DeleteCopies(theme);
            File.Copy(sourcePath, dest, overwrite: true);
        }
        return dest;
    }

    /// <summary>Apaga a copia do tema na pasta do store (so arquivos dentro dela).</summary>
    public void DeleteCopies(VictoryTheme theme)
    {
        if (Folder is not { } folder || !Directory.Exists(folder)) return;
        foreach (var ext in AudioExtensions)
        {
            try { File.Delete(Path.Combine(folder, theme.ToString().ToLowerInvariant() + ext)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    bool Stamp(out DateTime t, out long len)
    {
        t = DateTime.MinValue; len = -1;
        try { var fi = new FileInfo(FilePath!); if (!fi.Exists) return false; t = fi.LastWriteTimeUtc; len = fi.Length; return true; }
        catch (IOException) { return false; }
    }
}
