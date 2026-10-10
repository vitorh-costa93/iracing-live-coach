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

    /// <summary>Pede uma nova deteccao para o carro atual (chamar quando o carro/sessao muda). Ignorado se ja ha uma varredura em curso.</summary>
    public void Request(string carName, string className)
    {
        string model = PlayerNameStore.ModelKey(carName);
        if (model.Length == 0 || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        _ = Task.Factory.StartNew(() => Run(model, className, carName), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    void Run(string model, string className, string carName)
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            for (int attempt = 0; attempt < MaxAttempts && !_cts.IsCancellationRequested; attempt++)
            {
                var hits = GameLiveryProbe.Scan(_folders, n => _catalog.Any(e => string.Equals(e.Livery, n, StringComparison.OrdinalIgnoreCase)), _cts.Token);
                if (hits is not null && Resolve(hits, className, carName) is { } entry)
                {
                    var next = new Dictionary<string, PlayerNameEntry>(_byModel, StringComparer.OrdinalIgnoreCase)
                    {
                        [model] = new PlayerNameEntry { Model = model, Livery = entry.Livery, Name = entry.Driver, Country = entry.Country, Team = entry.Team },
                    };
                    _byModel = next;
                    Changed?.Invoke();
                    return;
                }
                if (_cts.Token.WaitHandle.WaitOne(RetryEvery)) return;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* a varredura e opcional: qualquer falha = sem deteccao */ }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    /// <summary>O jogo guarda registros de todas as pinturas ja carregadas (de varias classes): so contam as da classe do jogador, e so
    /// se restar uma unica pintura distinta (varias = sessao com IA ou residuo: nao chuta).</summary>
    LiveryEntry? Resolve(Dictionary<string, int> hits, string className, string carName)
    {
        if (className.Length == 0) return null;
        var mine = hits.Keys
            .Select(n => _catalog.FirstOrDefault(e => e.Class.Length > 0 && className.StartsWith(e.Class, StringComparison.OrdinalIgnoreCase)
                                                      && string.Equals(e.Livery, n, StringComparison.OrdinalIgnoreCase)))
            .Where(e => e is not null).ToList();
        if (mine.Count > 1) mine = mine.Where(e => FolderMatchesCar(e!.Model, carName)).ToList(); // mesma classe, carros diferentes (ex.: V8 Gen1 e Renault R26)
        return mine.Count == 1 ? mine[0] : null;
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
