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
    static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(15);

    readonly List<LiveryEntry> _catalog;
    readonly string[] _folders;
    readonly CancellationTokenSource _cts = new();
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
    }

    /// <summary>Identidade detectada para o modelo (CarName do jogo), ou null.</summary>
    public PlayerNameEntry? Get(string carName)
        => _byModel.TryGetValue(PlayerNameStore.ModelKey(carName), out var e) ? e : null;

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
        for (int attempt = 0; attempt < MaxAttempts && !_cts.IsCancellationRequested; attempt++)
        {
            var hits = GameLiveryProbe.Scan(_folders, n => _catalog.Any(e => string.Equals(e.Livery, n, StringComparison.OrdinalIgnoreCase)), _cts.Token);
            var entry = hits is null ? null : Resolve(hits, className, carName, attempt + 1, out _);
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
            if (_cts.Token.WaitHandle.WaitOne(RetryEvery)) return false;
            if (_pending is not null) return true;
        }
        return false;
    }

    /// <summary>O jogo guarda registros de todas as pinturas ja carregadas (de varias classes): so contam as da classe do jogador, e so
    /// se restar uma unica pintura distinta (varias = sessao com IA ou residuo: nao chuta). Cada chamada loga uma linha de diagnostico
    /// com o ranking por contagem (so informativo: nenhum desempate e aplicado, nao ha evidencia de que contagem/ordem identifique o jogador).</summary>
    LiveryEntry? Resolve(Dictionary<string, int> hits, string className, string carName, int attempt, out string reason)
    {
        LiveryEntry? result = null;
        string list = "";
        int sameClass = 0, afterFolder = 0;
        if (className.Length == 0) reason = "className vazio";
        else if (hits.Count == 0) reason = "0 hits do probe";
        else
        {
            var mine = hits.Keys
                .Select(n => _catalog.FirstOrDefault(e => e.Class.Length > 0 && className.StartsWith(e.Class, StringComparison.OrdinalIgnoreCase)
                                                          && string.Equals(e.Livery, n, StringComparison.OrdinalIgnoreCase)))
                .Where(e => e is not null).Select(e => e!).ToList();
            sameClass = mine.Count;
            list = string.Join("; ", mine.OrderByDescending(e => hits[e.Livery]).Take(20).Select(e => $"{e.Livery} [{e.Model}] x{hits[e.Livery]}"))
                   + (mine.Count > 20 ? $"; (+{mine.Count - 20})" : "");
            if (mine.Count > 1) mine = mine.Where(e => FolderMatchesCar(e.Model, carName)).ToList(); // mesma classe, carros diferentes (ex.: V8 Gen1 e Renault R26)
            afterFolder = mine.Count;
            if (mine.Count == 1) { result = mine[0]; reason = "ok"; }
            else reason = mine.Count == 0 ? "nenhuma candidata (classe/pasta)" : "varias candidatas";
        }
        Log($"tentativa {attempt}/{MaxAttempts} car='{carName}' class='{className}' hits={hits.Count} naClasse={sameClass} aposPasta={afterFolder} => {(result is null ? "null: " + reason : "ok: " + result.Livery)} | ranking: {list}");
        return result;
    }

    /// <summary>Pasta de Overrides x nome do carro: "formula_v8_g1_b" casa com "Formula V8 Gen1 Model1 (B)" e "renault_r26" com "Renault R26".
    /// Cada pedaco da pasta precisa aparecer no nome ("g1" = "gen1"; letra solta = fornecedor de pneus).</summary>
    public static bool FolderMatchesCar(string folder, string carName)
    {
        var sm = System.Text.RegularExpressions.Regex.Match(carName, @"\(([A-Za-z])\)");
        string supplier = sm.Success ? sm.Groups[1].Value.ToLowerInvariant() : "";
        var words = carName.ToLowerInvariant().Split([' ', '-', '(', ')', '_'], StringSplitOptions.RemoveEmptyEntries).Select(w => w.StartsWith("gen") ? "g" + w[3..] : w).ToHashSet();
        foreach (var part in folder.ToLowerInvariant().Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries))
            if (!(words.Contains(part) || (part.Length == 1 && part == supplier))) return false;
        return true;
    }

    public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
}
