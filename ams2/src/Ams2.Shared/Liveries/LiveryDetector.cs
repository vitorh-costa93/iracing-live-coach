using Ams2.Shared.PlayerNames;

namespace Ams2.Shared.Liveries;

/// <summary>
/// Detecta sozinho a pintura que o jogador escolheu no jogo (<see cref="GameLiveryProbe"/>) e a traduz em piloto/pais/equipe pelo
/// <see cref="LiveryCatalog"/>. A varredura roda em thread propria; <see cref="Get"/> e barato e nunca bloqueia. Sem deteccao devolve null
/// (o chamador cai na escolha manual).
/// </summary>
public sealed class LiveryDetector : IDisposable
{
    const int MaxAttempts = 4;
    const int SlowAttempts = 10; // depois das rapidas: a pintura pode so aparecer no heap apos o jogador confirmar o carro/entrar na pista
    static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(15);
    static readonly TimeSpan SlowRetryEvery = TimeSpan.FromSeconds(60);

    readonly List<LiveryEntry> _catalog;
    readonly string[] _folders;
    readonly Dictionary<string, LiveryEntry> _byDriver;
    readonly CancellationTokenSource _cts = new();
    volatile HashSet<string> _opponents = new(StringComparer.OrdinalIgnoreCase);
    volatile Dictionary<string, PlayerNameEntry> _byModel = new(StringComparer.OrdinalIgnoreCase);
    int _running; // 0/1

    /// <summary>Catalogo de pinturas lido do jogo (somente leitura).</summary>
    public IReadOnlyList<LiveryEntry> Catalog => _catalog;

    /// <summary>Disparado (em thread de fundo) quando uma pintura nova e detectada.</summary>
    public event Action? Changed;

    /// <summary>Detector para a pasta do jogo, ou null se nao ha catalogo (sem ele nao ha o que detectar).</summary>
    public static LiveryDetector? TryCreate(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !LiveryCatalog.IsGameRoot(gameRoot)) return null;
        var cat = LiveryCatalog.Load(gameRoot);
        return cat.Count == 0 ? null : new LiveryDetector(cat);
    }

    public LiveryDetector(List<LiveryEntry> catalog)
    {
        _catalog = catalog;
        _folders = catalog.Select(e => e.Model).Where(m => m.Contains('_') || m.Contains('-')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Piloto (nome do jogo) -> pintura, para dar pais e equipe aos carros da IA: o AMS2 so informa o nome. Entradas com pais e equipe vencem as vazias.
        _byDriver = new(StringComparer.OrdinalIgnoreCase);
        foreach (var e in catalog.OrderByDescending(e => (e.Country.Length > 0 ? 2 : 0) + (e.Team.Length > 0 ? 1 : 0)))
            if (e.Driver.Length > 0) _byDriver.TryAdd(e.Driver.Trim(), e);
    }

    /// <summary>Pais e equipe do piloto de IA pelo nome (catalogo de pinturas do jogo), ou null se o nome nao consta.</summary>
    public LiveryEntry? FindDriver(string name)
        => !string.IsNullOrWhiteSpace(name) && _byDriver.TryGetValue(name.Trim(), out var e) ? e : null;

    /// <summary>Identidade detectada para o modelo (CarName do jogo), ou null.</summary>
    public PlayerNameEntry? Get(string carName)
        => _byModel.TryGetValue(PlayerNameStore.ModelKey(carName), out var e) ? e : null;

    /// <summary>Nomes dos pilotos de IA da sessao: pinturas cujo piloto esta entre eles pertencem a carros da IA e nao ao jogador.</summary>
    public void SetOpponents(IEnumerable<string> names)
        => _opponents = new HashSet<string>(names.Select(n => n.Trim()).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);

    /// <summary>Esquece o que foi detectado (a pintura pode ter mudado fora da sessao).</summary>
    public void Forget() => _byModel = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pede uma nova deteccao para o carro atual (chamar quando o carro/sessao muda). Se ja ha uma varredura em curso, o pedido
    /// fica pendente e roda ao terminar (senao uma troca de carro/sessao durante a varredura se perderia).</summary>
    public void Request(string carName, string className)
    {
        string model = PlayerNameStore.ModelKey(carName);
        if (model.Length == 0) return;
        _pending = (model, className, carName);
        Log($"Request car='{carName}' class='{className}' model='{model}' pastas={_folders.Length} emCurso={_running != 0}");
        Start();
    }

    (string Model, string Class, string Car)? _pending;

    static readonly string LastFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ams2LiveCoach", "last-livery.txt");

    void SaveLast(string model, string livery)
    {
        try
        {
            var d = ReadLast(); d[model] = livery;
            File.WriteAllLines(LastFile, d.Select(kv => kv.Key + "	" + kv.Value));
        }
        catch { /* cache opcional */ }
    }

    static Dictionary<string, string> ReadLast()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var l in File.ReadAllLines(LastFile)) { var i = l.IndexOf('	'); if (i > 0) d[l[..i]] = l[(i + 1)..]; } } catch { }
        return d;
    }

    LiveryEntry? FromLast(string model)
    {
        if (!ReadLast().TryGetValue(model, out var liv)) return null;
        var e = _catalog.FirstOrDefault(c => string.Equals(c.Livery, liv, StringComparison.OrdinalIgnoreCase) && string.Equals(c.Model, model, StringComparison.OrdinalIgnoreCase))
                ?? _catalog.FirstOrDefault(c => string.Equals(c.Livery, liv, StringComparison.OrdinalIgnoreCase));
        if (e is null || _opponents.Contains(e.Driver.Trim())) return null;
        Log($"sem registro no heap: usando ultima pintura vista '{liv}' para '{model}'");
        return e;
    }

    void Start()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        _ = Task.Factory.StartNew(Run, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>Diagnostico persistente: Console.Error vai para %LOCALAPPDATA%\Ams2LiveCoach\host-out.log (mesmo mecanismo do host).</summary>
    static void Log(string msg) { try { Console.Error.WriteLine($"[Livery] {msg}"); } catch { /* log nunca derruba */ } }

    void Run()
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            while (!_cts.IsCancellationRequested && _pending is { } req)
            {
                _pending = null;
                if (RunOne(req.Model, req.Class, req.Car)) continue;
                if (_cts.IsCancellationRequested) return;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log($"falha: {ex.GetType().Name}: {ex.Message}"); /* a varredura e opcional: qualquer falha = sem deteccao */ }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            if (_pending is not null && !_cts.IsCancellationRequested) Start(); // pedido chegou entre o fim do laco e a liberacao
        }
    }

    /// <summary>Ate MaxAttempts varreduras para o carro; encerra cedo se chegar um pedido novo. Devolve true se foi substituido por um pedido novo.</summary>
    bool RunOne(string model, string className, string carName)
    {
        for (int attempt = 0; attempt < MaxAttempts + SlowAttempts && !_cts.IsCancellationRequested; attempt++)
        {
            var hits = GameLiveryProbe.Scan(_folders, n => _catalog.Any(e => string.Equals(e.Livery, n, StringComparison.OrdinalIgnoreCase)), _cts.Token);
            var entry = hits is null ? null : Resolve(hits, className, carName, attempt + 1, out _);
            if (entry is null && hits is { Count: 0 }) entry = FromLast(model); // em corrida o jogo nao deixa o registro no heap: usa a ultima pintura vista (menu/treino) se nao for de um carro da IA
            else if (entry is not null) SaveLast(model, entry.Livery);
            // (Resolve loga a tentativa; as lentas continuam ate achar ou chegar um pedido novo)
            if (hits is null) Log($"tentativa {attempt + 1}/{MaxAttempts} model='{model}': jogo nao aberto/sem acesso (null)");
            if (entry is not null)
            {
                var next = new Dictionary<string, PlayerNameEntry>(_byModel, StringComparer.OrdinalIgnoreCase)
                {
                    [model] = new PlayerNameEntry { Model = model, Livery = entry.Livery, Name = entry.Driver, Country = entry.Country, Team = entry.Team },
                };
                _byModel = next;
                Changed?.Invoke();
                return false;
            }
            if (_pending is not null) return true;
            if (_cts.Token.WaitHandle.WaitOne(attempt + 1 < MaxAttempts ? RetryEvery : SlowRetryEvery)) return false;
            if (_pending is not null) return true;
        }
        return false;
    }

    /// <summary>O jogo guarda registros de todas as pinturas ja carregadas (de varias classes): so contam as da classe do jogador, e so
    /// se restar uma unica pintura distinta (varias = sessao com IA ou residuo: nao chuta). Cada chamada loga uma linha de diagnostico
    /// com o ranking por contagem (so informativo: nenhum desempate e aplicado, nao ha evidencia de que contagem/ordem identifique o jogador).</summary>
    LiveryEntry? Resolve(Dictionary<string, int> hits, string className, string carName, int attempt, out string reason)
    {
        var result = Choose(_catalog, hits, className, carName, _opponents, out reason, out string list, out int sameClass, out int afterFolder);
        Log($"tentativa {attempt}/{MaxAttempts + SlowAttempts} car='{carName}' class='{className}' hits={hits.Count} naClasse={sameClass} aposPasta/IA={afterFolder} ia={_opponents.Count} => {(result is null ? "null: " + reason : "ok: " + result.Livery)} | ranking: {list}");
        return result;
    }

    /// <summary>Escolha pura (testavel): classe do jogador, depois pasta do modelo, depois descarta pinturas de pilotos que sao carros da IA na sessao
    /// (<paramref name="opponents"/>). So devolve quando sobra uma unica pintura; nunca chuta.</summary>
    public static LiveryEntry? Choose(IReadOnlyList<LiveryEntry> catalog, Dictionary<string, int> hits, string className, string carName, IReadOnlySet<string> opponents, out string reason)
        => Choose(catalog, hits, className, carName, opponents, out reason, out _, out _, out _);

    static LiveryEntry? Choose(IReadOnlyList<LiveryEntry> catalog, Dictionary<string, int> hits, string className, string carName, IReadOnlySet<string> opponents, out string reason, out string list, out int sameClass, out int afterFolder)
    {
        LiveryEntry? result = null;
        list = "";
        sameClass = 0; afterFolder = 0;
        if (className.Length == 0) reason = "className vazio";
        else if (hits.Count == 0) reason = "0 hits do probe";
        else
        {
            var mine = hits.Keys
                .Select(n => catalog.FirstOrDefault(e => e.Class.Length > 0 && className.StartsWith(e.Class, StringComparison.OrdinalIgnoreCase)
                                                         && string.Equals(e.Livery, n, StringComparison.OrdinalIgnoreCase)))
                .Where(e => e is not null).Select(e => e!).ToList();
            sameClass = mine.Count;
            list = string.Join("; ", mine.OrderByDescending(e => hits[e.Livery]).Take(20).Select(e => $"{e.Livery} [{e.Model}] x{hits[e.Livery]}"))
                   + (mine.Count > 20 ? $"; (+{mine.Count - 20})" : "");
            if (mine.Count > 1) mine = mine.Where(e => FolderMatchesCar(e.Model, carName)).ToList(); // mesma classe, carros diferentes (ex.: V8 Gen1 e Renault R26)
            if (mine.Count > 1 && opponents.Count > 0)
            {
                var notAi = mine.Where(e => !opponents.Contains(e.Driver.Trim())).ToList(); // pintura de piloto que e carro da IA nao e a do jogador
                if (notAi.Count > 0) mine = notAi;
            }
            afterFolder = mine.Count;
            if (mine.Count == 1) { result = mine[0]; reason = "ok"; }
            else reason = mine.Count == 0 ? "nenhuma candidata (classe/pasta)" : "varias candidatas";
        }
        return result;
    }

    /// <summary>Pasta de Overrides x nome do carro: "formula_v8_g1_b" casa com "Formula V8 Gen1 Model1 (B)" e "renault_r26" com "Renault R26".
    /// Cada pedaco da pasta precisa aparecer no nome ("g1" = "gen1"; letra solta = fornecedor de pneus).</summary>
    public static bool FolderMatchesCar(string folder, string carName)
    {
        var sm = System.Text.RegularExpressions.Regex.Match(carName, @"\(([A-Za-z])\)");
        string supplier = sm.Success ? sm.Groups[1].Value.ToLowerInvariant() : "";
        // O modelo oficial MP4/8 usa uma barra; a pasta de Overrides e mp48.
        // Normalize apenas barras internas ao identificador, mantendo os limites das palavras.
        string modelName = System.Text.RegularExpressions.Regex.Replace(carName.ToLowerInvariant(), @"(?<=[a-z0-9])/(?=[a-z0-9])", "");
        var words = modelName.Split([' ', '-', '(', ')', '_'], StringSplitOptions.RemoveEmptyEntries).Select(w => w.StartsWith("gen") ? "g" + w[3..] : w).ToHashSet();
        foreach (var part in folder.ToLowerInvariant().Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries))
            if (!(words.Contains(part) || (part.Length == 1 && part == supplier))) return false;
        return true;
    }

    public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
}
