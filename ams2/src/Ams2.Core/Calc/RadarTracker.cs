namespace Ams2.Core.Calc;

/// <summary>Proximidade de um carro: Far (cinza), Warn (ambar, |frente| ate 12 m) ou Alert (vermelho, lado a lado, |frente| ate 7 m). Mesmas janelas do Radar do V3.</summary>
public enum RadarZone : byte { Far, Warn, Alert }

/// <summary>Faixa do carro em relacao ao jogador (lateral real do AMS2): Center = |lateral| menor que 1,2 m (na mesma linha).</summary>
public enum RadarSide : byte { Center, Left, Right }

/// <summary>
/// Opcoes do radar (imutavel; trocar a referencia no tracker aplica na hora). Alcance = metros para frente e para tras que cabem no painel.
/// Sensibilidade = multiplicador das janelas de aviso (7 m vermelho / 12 m ambar do V3): 0,5 (so quase encostado) a 1,5 (avisa cedo).
/// </summary>
public sealed record RadarOptions
{
    public const double MinRange = 10, MaxRange = 40, DefaultRange = 15;
    public const double MinSensitivity = 0.5, MaxSensitivity = 1.5;
    /// <summary>Distancia a frente/atras (m) dentro da qual um carro ao lado e Alert / qualquer carro e Warn (valores do V3 com sensibilidade 1).</summary>
    public const double AlertLongMeters = 7.0, WarnLongMeters = 12.0;
    /// <summary>Lateral (centro a centro): menor que SideMin = na mesma linha; ate AlertLateralMeters conta como "ao lado".</summary>
    public const double SideMinMeters = 1.2, AlertLateralMeters = 4.5;

    public double RangeMeters { get; init; } = DefaultRange;
    /// <summary>Meia-largura lateral do painel (m): carros mais afastados nao entram.</summary>
    public double LateralMeters { get; init; } = 7.5;
    public double Sensitivity { get; init; } = 1.0;
    /// <summary>Tamanho do carro (m). O AMS2 nao informa dimensoes: valor unico (~5,0 x 2,0 m).</summary>
    public double CarLengthMeters { get; init; } = 5.0;
    public double CarWidthMeters { get; init; } = 2.0;
    /// <summary>Carros mais proximos mantidos por quadro (a capacidade do quadro e fixa em <see cref="RadarFrame.Capacity"/>).</summary>
    public int MaxCars { get; init; } = RadarFrame.Capacity;

    public RadarOptions Normalized() => this with
    {
        RangeMeters = Clamp(RangeMeters, MinRange, MaxRange, DefaultRange),
        LateralMeters = Clamp(LateralMeters, 3, 15, 7.5),
        Sensitivity = Clamp(Sensitivity, MinSensitivity, MaxSensitivity, 1.0),
        CarLengthMeters = Clamp(CarLengthMeters, 1, 8, 5.0),
        CarWidthMeters = Clamp(CarWidthMeters, 0.5, 3.5, 2.0),
        MaxCars = Math.Clamp(MaxCars, 1, RadarFrame.Capacity),
    };

    static double Clamp(double v, double lo, double hi, double fallback) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : fallback;
}

/// <summary>
/// Um carro no referencial do jogador (frente +, direita +). VForward/VRight = velocidade relativa (m/s) no mesmo referencial, usada pelo
/// widget para extrapolar a posicao entre dois passos do provider.
/// </summary>
public readonly struct RadarCar
{
    public readonly int Index;
    public readonly float Forward, Right, RelHeading, VForward, VRight, RelSpeed;
    public readonly RadarZone Zone;
    public readonly RadarSide Side;

    public RadarCar(int index, float forward, float right, float relHeading, float vForward, float vRight, float relSpeed, RadarZone zone, RadarSide side)
    {
        Index = index; Forward = forward; Right = right; RelHeading = relHeading; VForward = vForward; VRight = vRight; RelSpeed = relSpeed; Zone = zone; Side = side;
    }
}

/// <summary>
/// Saida do <see cref="RadarTracker"/>. Imutavel para quem le: o tracker so preenche o quadro antes de publica-lo e so o reutiliza depois
/// de <see cref="RadarTracker.RingSize"/> - 1 publicacoes seguintes (ate la o consumidor deve ter terminado o desenho, o que leva microssegundos).
/// Assim nao ha alocacao por quadro a 60 Hz ou mais.
/// </summary>
public sealed class RadarFrame
{
    public const int Capacity = 16;
    readonly RadarCar[] _cars = new RadarCar[Capacity];

    /// <summary>Sem dados: fora de sessao, sem pose do jogador ou sem outros carros na pista. O widget nao desenha.</summary>
    public static readonly RadarFrame Empty = new();

    /// <summary>true = jogador com pose valida e ao menos um outro carro na pista (o radar tem sentido).</summary>
    public bool Valid { get; private set; }
    /// <summary>Relogio do provider no instante da leitura (s).</summary>
    public double Time { get; private set; }
    public int Count { get; private set; }
    public double RangeMeters { get; private set; } = RadarOptions.DefaultRange;
    public double LateralMeters { get; private set; } = 7.5;
    public double CarLengthMeters { get; private set; } = 5.0;
    public double CarWidthMeters { get; private set; } = 2.0;
    /// <summary>Ha carro em Alert a esquerda / a direita (barras vermelhas laterais).</summary>
    public bool AlertLeft { get; private set; }
    public bool AlertRight { get; private set; }
    /// <summary>Distancia a frente (m, + = a frente) do carro Alert mais proximo de cada lado; NaN = nenhum.</summary>
    public double LeftOffset { get; private set; } = double.NaN;
    public double RightOffset { get; private set; } = double.NaN;
    /// <summary>Velocidade do jogador (m/s).</summary>
    public double PlayerSpeed { get; private set; }

    public ReadOnlySpan<RadarCar> Cars => _cars.AsSpan(0, Count);
    /// <summary>Algum carro dentro do alcance (gatilho do modo "so quando houver carro proximo").</summary>
    public bool AnyNear => Count > 0;

    internal void Reset(bool valid, double time, RadarOptions o)
    {
        Valid = valid; Time = time; Count = 0;
        RangeMeters = o.RangeMeters; LateralMeters = o.LateralMeters; CarLengthMeters = o.CarLengthMeters; CarWidthMeters = o.CarWidthMeters;
        AlertLeft = AlertRight = false; LeftOffset = RightOffset = double.NaN; PlayerSpeed = 0;
    }

    internal void Set(ReadOnlySpan<RadarCar> cars, double playerSpeed)
    {
        cars.CopyTo(_cars);
        Count = cars.Length;
        PlayerSpeed = playerSpeed;
        foreach (ref readonly var c in cars)
        {
            if (c.Zone != RadarZone.Alert) continue;
            if (c.Side == RadarSide.Left) { AlertLeft = true; if (double.IsNaN(LeftOffset) || Math.Abs(c.Forward) < Math.Abs(LeftOffset)) LeftOffset = c.Forward; }
            else if (c.Side == RadarSide.Right) { AlertRight = true; if (double.IsNaN(RightOffset) || Math.Abs(c.Forward) < Math.Abs(RightOffset)) RightOffset = c.Forward; }
        }
    }
}

/// <summary>
/// Radar lateral puro: do <see cref="SessionSnapshot"/> (posicao de mundo + yaw de cada carro) calcula onde cada carro esta no referencial do jogador.
/// Rumo de avanco = yaw + pi (confirmado em sessao real, ver ams2/reference/radar-notes.md): avanco (x, z) = (-sin yaw, -cos yaw), direita = (-cos yaw, sin yaw).
/// Sem alocacao por chamada (vetores fixos + anel de quadros reaproveitados). Uma thread escreve (<see cref="Update"/>); qualquer thread le <see cref="Current"/>.
/// </summary>
public sealed class RadarTracker
{
    public const int RingSize = 4;
    const double ParallelTrackHeightMeters = 3.5;

    readonly RadarFrame[] _ring = new RadarFrame[RingSize];
    readonly RadarCar[] _scratch = new RadarCar[RadarFrame.Capacity];
    int _next;
    RadarFrame _current = RadarFrame.Empty;
    RadarOptions _options = new RadarOptions().Normalized();

    public RadarTracker() { for (int i = 0; i < RingSize; i++) _ring[i] = new RadarFrame(); }

    /// <summary>Opcoes em vigor (troca atomica; vale a partir do proximo <see cref="Update"/>).</summary>
    public RadarOptions Options { get => Volatile.Read(ref _options); set => Volatile.Write(ref _options, (value ?? new RadarOptions()).Normalized()); }

    /// <summary>Ultimo quadro publicado (<see cref="RadarFrame.Empty"/> antes do primeiro).</summary>
    public RadarFrame Current => Volatile.Read(ref _current);

    public void Reset() => Volatile.Write(ref _current, RadarFrame.Empty);

    public RadarFrame Update(SessionSnapshot s, double time)
    {
        var o = Options;
        var frame = _ring[_next];
        _next = (_next + 1) % RingSize;

        var cars = s.Cars;
        int n = cars.Count;
        CarSnapshot? player = null;
        bool others = false;
        if (s.InSession && s.Player is { } pl)
            for (int i = 0; i < n; i++)
            {
                var c = cars[i];
                if (c.Index == pl.Index) player = c;
                else if (!c.InGarage) others = true;
            }

        if (player is null || !player.HasPose || !others)
        {
            frame.Reset(false, time, o);
            Volatile.Write(ref _current, frame);
            return frame;
        }

        frame.Reset(true, time, o);
        double sinH = -Math.Sin(player.Yaw), cosH = -Math.Cos(player.Yaw);   // rumo h = yaw + pi: sin h = -sin yaw, cos h = -cos yaw
        double vp = player.SpeedMps;
        double alertLong = RadarOptions.AlertLongMeters * o.Sensitivity, warnLong = RadarOptions.WarnLongMeters * o.Sensitivity;
        double maxLap = o.RangeMeters * 3 + 20;
        int count = 0;

        for (int i = 0; i < n; i++)
        {
            var c = cars[i];
            if (c.Index == player.Index || c.InGarage || !c.HasPose) continue;
            // Carro nos boxes so e vizinho se o jogador tambem esta (igual ao V3).
            if (c.InPitLane != player.InPitLane) continue;

            double dx = c.PosX - player.PosX, dz = c.PosZ - player.PosZ;
            double fwd = dx * sinH + dz * cosH;
            double right = dx * cosH - dz * sinH;
            if (Math.Abs(fwd) > o.RangeMeters || Math.Abs(right) > o.LateralMeters) continue;
            if (Math.Abs(c.PosY - player.PosY) > ParallelTrackHeightMeters) continue;
            if (s.TrackLength > 0)
            {
                double dl = Math.Abs(c.LapDistance - player.LapDistance);
                dl = Math.Min(dl, s.TrackLength - dl);
                if (dl > maxLap) continue;   // trecho paralelo / viaduto: perto no mapa, longe na volta
            }

            double rel = Math.IEEERemainder(c.Yaw - player.Yaw, 2 * Math.PI);   // diferenca de rumo (+ = virou para a direita)
            double v = c.SpeedMps;
            double absRight = Math.Abs(right), absFwd = Math.Abs(fwd);
            var side = absRight < RadarOptions.SideMinMeters ? RadarSide.Center : right < 0 ? RadarSide.Left : RadarSide.Right;
            var zone = side != RadarSide.Center && absFwd <= alertLong && absRight <= RadarOptions.AlertLateralMeters ? RadarZone.Alert
                : absFwd <= warnLong ? RadarZone.Warn : RadarZone.Far;

            var item = new RadarCar(c.Index, (float)fwd, (float)right, (float)rel,
                vForward: (float)(v * Math.Cos(rel) - vp), vRight: (float)(v * Math.Sin(rel)), relSpeed: (float)(v - vp), zone, side);

            // Insercao ordenada por |frente| (mais proximos primeiro) em vetor fixo.
            int max = o.MaxCars;
            int pos = count;
            while (pos > 0 && Math.Abs(_scratch[pos - 1].Forward) > absFwd) pos--;
            if (count < max) { for (int k = count; k > pos; k--) _scratch[k] = _scratch[k - 1]; _scratch[pos] = item; count++; }
            else if (pos < count) { for (int k = count - 1; k > pos; k--) _scratch[k] = _scratch[k - 1]; _scratch[pos] = item; }
        }

        frame.Set(_scratch.AsSpan(0, count), vp);
        Volatile.Write(ref _current, frame);
        return frame;
    }
}
