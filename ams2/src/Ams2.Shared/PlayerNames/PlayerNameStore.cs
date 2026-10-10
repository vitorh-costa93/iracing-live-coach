using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ams2.Shared.PlayerNames;

/// <summary>Um modelo de carro ja visto com o jogador ao volante. <c>Name</c> vazio = sem nome de exibicao.</summary>
public sealed record PlayerNameEntry
{
    /// <summary>Modelo (CarName do jogo sem o sufixo de fornecedor).</summary>
    public string Model { get; init; } = "";
    /// <summary>Nome que o jogo da ao carro do jogador (perfil do jogo), ex.: "Vitor COSTA".</summary>
    public string OriginalName { get; init; } = "";
    /// <summary>Pintura escolhida no catalogo (livery_name do jogo); "" = escolha manual / nenhuma.</summary>
    public string Livery { get; init; } = "";
    /// <summary>Nome de exibicao (piloto da pintura); "" = usar o do jogo.</summary>
    public string Name { get; init; } = "";
    /// <summary>Pais do piloto (sigla de 3 letras do jogo, ex.: "FIN"); "" = desconhecido.</summary>
    public string Country { get; init; } = "";
    /// <summary>Equipe da pintura; "" = deduzir do nome do carro.</summary>
    public string Team { get; init; } = "";
    /// <summary>true se alguma coisa substitui o que o jogo informa.</summary>
    [JsonIgnore] public bool HasOverride => Name.Length > 0 || Country.Length > 0 || Team.Length > 0;
}

/// <summary>Estado publicado ao Control Center: carro atual detectado e a lista de modelos.</summary>
public sealed record PlayerNamesState
{
    /// <summary>Modelo do carro atual do jogador ("" = nenhum detectado agora).</summary>
    public string CurrentModel { get; init; } = "";
    public List<PlayerNameEntry> Entries { get; init; } = [];
}

/// <summary>
/// Mapa global modelo-de-carro -> identidade de exibicao do jogador (piloto, pais, equipe da pintura escolhida), em <c>{raiz}\player-names.json</c> (raiz padrao
/// %AppData%\ams2-live-coach), mais a lista de modelos ja vistos com o jogador ao volante. Chave = CarName sem o sufixo
/// "(M)"/"(B)", comparada sem diferenciar caixa. Escrita atomica; relê o arquivo quando ele muda por fora (<see cref="Refresh"/>).
/// Sem arquivo (<see cref="InMemory"/>) nada e gravado (--png, testes).
/// </summary>
public sealed class PlayerNameStore
{
    public const int SchemaVersion = 1;
    public const string FileName = "player-names.json";

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    sealed record FileModel
    {
        public int Version { get; init; } = SchemaVersion;
        public List<PlayerNameEntry> Entries { get; init; } = [];
    }

    readonly object _gate = new();
    readonly string? _path;
    readonly Dictionary<string, PlayerNameEntry> _entries = new(StringComparer.OrdinalIgnoreCase); // chave normalizada
    DateTime _stamp = DateTime.MinValue;
    long _length = -1;
    volatile Dictionary<string, PlayerNameEntry> _names = new(StringComparer.OrdinalIgnoreCase); // so entradas com algum override, leitura sem lock
    string _current = "";

    /// <summary>Disparado (em qualquer thread) quando a lista ou um nome muda.</summary>
    public event Action? Changed;

    public PlayerNameStore(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ams2-live-coach");
        _path = Path.Combine(root, FileName);
        Load();
    }

    PlayerNameStore() { }
    /// <summary>Store sem arquivo.</summary>
    public static PlayerNameStore InMemory() => new();

    public string? FilePath => _path;

    /// <summary>CarName do jogo sem o sufixo de fornecedor "(M)"/"(B)" (e sem espacos nas pontas).</summary>
    public static string ModelKey(string carName)
    {
        var n = (carName ?? "").Trim();
        if (n.EndsWith("(M)", StringComparison.Ordinal) || n.EndsWith("(B)", StringComparison.Ordinal)) n = n[..^3].TrimEnd();
        return n;
    }

    /// <summary>Identidade de exibicao do modelo, ou null. Barato (sem lock): chamado a cada passo do provider.</summary>
    public PlayerNameEntry? Get(string carName)
        => _names.TryGetValue(ModelKey(carName), out var n) ? n : null;

    public PlayerNamesState State()
    {
        lock (_gate)
            return new PlayerNamesState { CurrentModel = _current, Entries = _entries.Values.OrderBy(e => e.Model, StringComparer.CurrentCultureIgnoreCase).ToList() };
    }

    /// <summary>Define so o nome de exibicao (mantem pais/equipe/pintura). Nome vazio limpa o nome.</summary>
    public void Set(string carName, string? name)
        => Update(carName, e => e with { Name = Clean(name) });

    /// <summary>Define a identidade inteira do modelo (pintura do catalogo ou digitada). Tudo vazio equivale a <see cref="Clear"/>.</summary>
    public void SetIdentity(string carName, string? livery, string? name, string? country, string? team)
        => Update(carName, e => e with { Livery = Clean(livery), Name = Clean(name), Country = Clean(country), Team = Clean(team) });

    public void Clear(string carName) => SetIdentity(carName, "", "", "", "");

    void Update(string carName, Func<PlayerNameEntry, PlayerNameEntry> edit)
    {
        string model = ModelKey(carName);
        if (model.Length == 0) return;
        bool changed;
        lock (_gate)
        {
            _entries.TryGetValue(model, out var cur);
            var next = edit(cur ?? new PlayerNameEntry { Model = model });
            if (cur is null && !next.HasOverride) return;
            changed = !next.Equals(cur);
            if (!changed) return;
            _entries[model] = next;
            Rebuild(); Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Registra o carro do jogador agora: modelo e nome original do jogo.
    /// So grava em disco quando algo muda. <paramref name="carName"/> vazio = nenhum carro detectado.</summary>
    public void Observe(string carName, string originalName)
    {
        string model = ModelKey(carName);
        bool changed = false;
        lock (_gate)
        {
            if (_current != model) { _current = model; changed = true; }
            if (model.Length > 0)
            {
                _entries.TryGetValue(model, out var cur);
                var next = (cur ?? new PlayerNameEntry { Model = model }) with
                {
                    OriginalName = string.IsNullOrWhiteSpace(originalName) ? cur?.OriginalName ?? "" : originalName.Trim(),
                };
                if (!next.Equals(cur)) { _entries[model] = next; Save(); changed = true; }
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Relê o arquivo se ele mudou por fora (edicao manual). Barato: so compara data/tamanho.</summary>
    public void Refresh()
    {
        if (_path is null) return;
        bool changed;
        lock (_gate)
        {
            if (!Stamp(out var t, out var len) || (t == _stamp && len == _length)) return;
            var before = _entries.Values.OrderBy(e => e.Model, StringComparer.OrdinalIgnoreCase).ToList();
            Load();
            changed = !before.SequenceEqual(_entries.Values.OrderBy(e => e.Model, StringComparer.OrdinalIgnoreCase));
        }
        if (changed) Changed?.Invoke();
    }

    // ---- internos (sob _gate) ----

    static string Clean(string? n) => (n ?? "").Trim();

    void Rebuild()
    {
        var d = new Dictionary<string, PlayerNameEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _entries.Values) if (e.HasOverride) d[ModelKey(e.Model)] = e;
        _names = d;
    }

    bool Stamp(out DateTime t, out long len)
    {
        t = DateTime.MinValue; len = -1;
        try { var fi = new FileInfo(_path!); if (!fi.Exists) return false; t = fi.LastWriteTimeUtc; len = fi.Length; return true; }
        catch (IOException) { return false; }
    }

    void Load()
    {
        if (_path is null) return;
        if (!Stamp(out _stamp, out _length)) return;
        try
        {
            var m = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), Json);
            if (m is null || m.Version > SchemaVersion) return; // corrompido ou de esquema mais novo: nao adivinha
            _entries.Clear();
            foreach (var e in m.Entries)
            {
                string model = ModelKey(e.Model);
                if (model.Length == 0) continue;
                _entries[model] = new PlayerNameEntry { Model = model, OriginalName = Clean(e.OriginalName), Livery = Clean(e.Livery), Name = Clean(e.Name), Country = Clean(e.Country), Team = Clean(e.Team) };
            }
            Rebuild();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
    }

    void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var model = new FileModel { Entries = _entries.Values.OrderBy(e => e.Model, StringComparer.OrdinalIgnoreCase).ToList() };
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(model, Json));
            File.Move(tmp, _path, overwrite: true);
            Stamp(out _stamp, out _length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
