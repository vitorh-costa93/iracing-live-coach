using System;
using System.Collections.Generic;
using System.Globalization;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Short instructor-style phrases in en, pt-BR, es, de, fr. Templates use {name} placeholders.
/// Corner tips use the key "tip.&lt;CornerIssue&gt;".</summary>
public static class CoachPhrases
{
    public static readonly IReadOnlyList<string> Languages = new[] { "en", "pt-BR", "es", "de", "fr" };

    private static readonly Dictionary<string, Dictionary<string, string>> Table = new()
    {
        ["en"] = new()
        {
            ["tip.BrakeTooEarly"] = "Brake later into turn {turn}, about {m} meters.",
            ["tip.BrakeTooLate"] = "Brake earlier into turn {turn}, about {m} meters.",
            ["tip.BrakeTooSoft"] = "More brake pressure into turn {turn}.",
            ["tip.TrailBrakeShort"] = "Trail the brake deeper into turn {turn}.",
            ["tip.MinSpeedLow"] = "Carry more speed through turn {turn}, about {kmh} kilometers per hour.",
            ["tip.ThrottleLate"] = "Throttle earlier out of turn {turn}.",
            ["praise"] = "Good, turn {turn} is better. {gain} seconds found.",
            ["summary.best"] = "New best lap, {lap}.",
            ["summary.delta"] = "{lap}, {delta} seconds off your best. Biggest loss turn {turn}.",
            ["summary.clean"] = "{lap}, {delta} seconds off your best.",
            ["briefing.ref"] = "Reference loaded, best {lap}. Let's go.",
            ["briefing.noref"] = "No reference yet. Drive a clean lap to set one.",
            ["answer.lastlap"] = "Last lap {lap}, {delta} seconds off your best.",
            ["answer.nolap"] = "No lap completed yet.",
            ["answer.worstcorner"] = "Turn {turn} costs the most, {loss} seconds. {tip}",
            ["answer.noloss"] = "No corner is losing time. Keep it up.",
            ["answer.score"] = "Session score {score}. Last lap {last}.",
            ["answer.noscore"] = "No score yet. Complete a clean lap.",
            ["answer.focus"] = "This week, focus on {focus}.",
            ["answer.nofocus"] = "Not enough laps yet for a focus.",
            ["answer.repeat.none"] = "Nothing to repeat.",
            ["answer.unknown"] = "Sorry, I did not get that.",
            ["summary.head"] = "Lap {lap}.",
            ["summary.corner"] = "{loss} seconds in turn {turn}. {tip}",
            ["summary.nocorner"] = "No time lost in the corners.",
            ["focus.Braking"] = "braking",
            ["focus.TrailBraking"] = "trail braking",
            ["focus.Throttle"] = "throttle application",
            ["focus.Consistency"] = "consistency",
        },
        ["pt-BR"] = new()
        {
            ["tip.BrakeTooEarly"] = "Frenagem mais tarde na curva {turn}, uns {m} metros.",
            ["tip.BrakeTooLate"] = "Frenagem mais cedo na curva {turn}, uns {m} metros.",
            ["tip.BrakeTooSoft"] = "Mais pressão no freio na curva {turn}.",
            ["tip.TrailBrakeShort"] = "Leve o freio mais fundo na curva {turn}.",
            ["tip.MinSpeedLow"] = "Mais velocidade na curva {turn}, uns {kmh} quilômetros por hora.",
            ["tip.ThrottleLate"] = "Acelere mais cedo na saída da curva {turn}.",
            ["praise"] = "Boa, a curva {turn} melhorou. {gain} segundos ganhos.",
            ["summary.best"] = "Nova melhor volta, {lap}.",
            ["summary.delta"] = "{lap}, {delta} segundos da sua melhor. Maior perda na curva {turn}.",
            ["summary.clean"] = "{lap}, {delta} segundos da sua melhor.",
            ["briefing.ref"] = "Referência carregada, melhor volta {lap}. Vamos lá.",
            ["briefing.noref"] = "Sem referência ainda. Faça uma volta limpa para criar uma.",
            ["answer.lastlap"] = "Última volta {lap}, {delta} segundos da sua melhor.",
            ["answer.nolap"] = "Nenhuma volta completa ainda.",
            ["answer.worstcorner"] = "A curva {turn} custa mais, {loss} segundos. {tip}",
            ["answer.noloss"] = "Nenhuma curva está perdendo tempo. Continue assim.",
            ["answer.score"] = "Nota da sessão {score}. Última volta {last}.",
            ["answer.noscore"] = "Sem nota ainda. Complete uma volta limpa.",
            ["answer.focus"] = "Nesta semana, foque em {focus}.",
            ["answer.nofocus"] = "Poucas voltas ainda para definir um foco.",
            ["answer.repeat.none"] = "Nada para repetir.",
            ["answer.unknown"] = "Desculpe, não entendi.",
            ["summary.head"] = "Volta {lap}.",
            ["summary.corner"] = "{loss} segundos na curva {turn}. {tip}",
            ["summary.nocorner"] = "Nenhum tempo perdido nas curvas.",
            ["focus.Braking"] = "frenagem",
            ["focus.TrailBraking"] = "freio na entrada da curva",
            ["focus.Throttle"] = "aceleração",
            ["focus.Consistency"] = "consistência",
        },
        ["es"] = new()
        {
            ["tip.BrakeTooEarly"] = "Frena más tarde en la curva {turn}, unos {m} metros.",
            ["tip.BrakeTooLate"] = "Frena antes en la curva {turn}, unos {m} metros.",
            ["tip.BrakeTooSoft"] = "Más presión de freno en la curva {turn}.",
            ["tip.TrailBrakeShort"] = "Lleva el freno más adentro en la curva {turn}.",
            ["tip.MinSpeedLow"] = "Lleva más velocidad en la curva {turn}, unos {kmh} kilómetros por hora.",
            ["tip.ThrottleLate"] = "Acelera antes a la salida de la curva {turn}.",
            ["praise"] = "Bien, la curva {turn} mejoró. {gain} segundos ganados.",
            ["summary.best"] = "Nueva mejor vuelta, {lap}.",
            ["summary.delta"] = "{lap}, {delta} segundos de tu mejor. Mayor pérdida en la curva {turn}.",
            ["summary.clean"] = "{lap}, {delta} segundos de tu mejor.",
            ["briefing.ref"] = "Referencia cargada, mejor vuelta {lap}. Vamos.",
            ["briefing.noref"] = "Aún no hay referencia. Haz una vuelta limpia para crearla.",
            ["answer.lastlap"] = "Última vuelta {lap}, {delta} segundos de tu mejor.",
            ["answer.nolap"] = "Aún no hay vueltas completas.",
            ["answer.worstcorner"] = "La curva {turn} es la que más cuesta, {loss} segundos. {tip}",
            ["answer.noloss"] = "Ninguna curva pierde tiempo. Sigue así.",
            ["answer.score"] = "Puntuación de la sesión {score}. Última vuelta {last}.",
            ["answer.noscore"] = "Aún no hay puntuación. Completa una vuelta limpia.",
            ["answer.focus"] = "Esta semana, céntrate en {focus}.",
            ["answer.nofocus"] = "Aún faltan vueltas para definir un foco.",
            ["answer.repeat.none"] = "Nada que repetir.",
            ["answer.unknown"] = "Perdón, no te entendí.",
            ["summary.head"] = "Vuelta {lap}.",
            ["summary.corner"] = "{loss} segundos en la curva {turn}. {tip}",
            ["summary.nocorner"] = "Sin tiempo perdido en las curvas.",
            ["focus.Braking"] = "la frenada",
            ["focus.TrailBraking"] = "el freno en la entrada",
            ["focus.Throttle"] = "la aceleración",
            ["focus.Consistency"] = "la consistencia",
        },
        ["de"] = new()
        {
            ["tip.BrakeTooEarly"] = "Später bremsen in Kurve {turn}, etwa {m} Meter.",
            ["tip.BrakeTooLate"] = "Früher bremsen in Kurve {turn}, etwa {m} Meter.",
            ["tip.BrakeTooSoft"] = "Mehr Bremsdruck in Kurve {turn}.",
            ["tip.TrailBrakeShort"] = "Die Bremse tiefer in Kurve {turn} tragen.",
            ["tip.MinSpeedLow"] = "Mehr Speed durch Kurve {turn}, etwa {kmh} Stundenkilometer.",
            ["tip.ThrottleLate"] = "Früher aufs Gas aus Kurve {turn}.",
            ["praise"] = "Gut, Kurve {turn} ist besser. {gain} Sekunden gefunden.",
            ["summary.best"] = "Neue Bestzeit, {lap}.",
            ["summary.delta"] = "{lap}, {delta} Sekunden hinter deiner Bestzeit. Größter Verlust in Kurve {turn}.",
            ["summary.clean"] = "{lap}, {delta} Sekunden hinter deiner Bestzeit.",
            ["briefing.ref"] = "Referenz geladen, Bestzeit {lap}. Los geht's.",
            ["briefing.noref"] = "Noch keine Referenz. Fahr eine saubere Runde.",
            ["answer.lastlap"] = "Letzte Runde {lap}, {delta} Sekunden hinter deiner Bestzeit.",
            ["answer.nolap"] = "Noch keine Runde beendet.",
            ["answer.worstcorner"] = "Kurve {turn} kostet am meisten, {loss} Sekunden. {tip}",
            ["answer.noloss"] = "Keine Kurve verliert Zeit. Weiter so.",
            ["answer.score"] = "Session-Wertung {score}. Letzte Runde {last}.",
            ["answer.noscore"] = "Noch keine Wertung. Fahr eine saubere Runde.",
            ["answer.focus"] = "Diese Woche Fokus auf {focus}.",
            ["answer.nofocus"] = "Noch zu wenige Runden für einen Fokus.",
            ["answer.repeat.none"] = "Nichts zu wiederholen.",
            ["answer.unknown"] = "Entschuldigung, das habe ich nicht verstanden.",
            ["summary.head"] = "Runde {lap}.",
            ["summary.corner"] = "{loss} Sekunden in Kurve {turn}. {tip}",
            ["summary.nocorner"] = "Keine Zeit in den Kurven verloren.",
            ["focus.Braking"] = "das Bremsen",
            ["focus.TrailBraking"] = "das Trail-Braking",
            ["focus.Throttle"] = "das Gasgeben",
            ["focus.Consistency"] = "die Konstanz",
        },
        ["fr"] = new()
        {
            ["tip.BrakeTooEarly"] = "Freine plus tard au virage {turn}, environ {m} mètres.",
            ["tip.BrakeTooLate"] = "Freine plus tôt au virage {turn}, environ {m} mètres.",
            ["tip.BrakeTooSoft"] = "Plus de pression de frein au virage {turn}.",
            ["tip.TrailBrakeShort"] = "Garde le frein plus loin dans le virage {turn}.",
            ["tip.MinSpeedLow"] = "Plus de vitesse dans le virage {turn}, environ {kmh} kilomètres heure.",
            ["tip.ThrottleLate"] = "Accélère plus tôt en sortie du virage {turn}.",
            ["praise"] = "Bien, le virage {turn} est meilleur. {gain} secondes gagnées.",
            ["summary.best"] = "Nouveau meilleur tour, {lap}.",
            ["summary.delta"] = "{lap}, {delta} secondes de ton meilleur. Plus grosse perte au virage {turn}.",
            ["summary.clean"] = "{lap}, {delta} secondes de ton meilleur.",
            ["briefing.ref"] = "Référence chargée, meilleur tour {lap}. C'est parti.",
            ["briefing.noref"] = "Pas encore de référence. Fais un tour propre pour en créer une.",
            ["answer.lastlap"] = "Dernier tour {lap}, {delta} secondes de ton meilleur.",
            ["answer.nolap"] = "Aucun tour terminé pour l'instant.",
            ["answer.worstcorner"] = "Le virage {turn} coûte le plus, {loss} secondes. {tip}",
            ["answer.noloss"] = "Aucun virage ne perd de temps. Continue comme ça.",
            ["answer.score"] = "Score de session {score}. Dernier tour {last}.",
            ["answer.noscore"] = "Pas encore de score. Fais un tour propre.",
            ["answer.focus"] = "Cette semaine, concentre-toi sur {focus}.",
            ["answer.nofocus"] = "Pas assez de tours pour un objectif.",
            ["answer.repeat.none"] = "Rien à répéter.",
            ["answer.unknown"] = "Désolé, je n'ai pas compris.",
            ["summary.head"] = "Tour {lap}.",
            ["summary.corner"] = "{loss} secondes au virage {turn}. {tip}",
            ["summary.nocorner"] = "Aucun temps perdu dans les virages.",
            ["focus.Braking"] = "le freinage",
            ["focus.TrailBraking"] = "le freinage dégressif",
            ["focus.Throttle"] = "l'accélération",
            ["focus.Consistency"] = "la régularité",
        },
    };

    /// <summary>Maps "pt", "pt_BR", "PT-br", "de-DE"... to a supported language; unknown -> "en".</summary>
    public static string Normalize(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return "en";
        var l = lang.Trim().Replace('_', '-');
        foreach (var s in Languages) if (string.Equals(s, l, StringComparison.OrdinalIgnoreCase)) return s;
        var prefix = l.Split('-')[0].ToLowerInvariant();
        foreach (var s in Languages) if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return s;
        return "en";
    }

    public static bool HasKey(string key) => Table["en"].ContainsKey(key);

    public static string Render(string lang, string key, IReadOnlyDictionary<string, string>? args = null)
    {
        var l = Normalize(lang);
        if (!Table[l].TryGetValue(key, out var template) && !Table["en"].TryGetValue(key, out template))
            return key;
        if (args != null)
            foreach (var kv in args) template = template.Replace("{" + kv.Key + "}", kv.Value);
        return template;
    }

    public static CultureInfo Culture(string lang)
    {
        try { return CultureInfo.GetCultureInfo(Normalize(lang)); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    public static string Number(string lang, double value, int decimals) =>
        Math.Round(value, decimals).ToString("F" + decimals, Culture(lang));

    /// <summary>"1:32.4" (culture decimal separator).</summary>
    public static string LapTime(string lang, double sec)
    {
        if (sec <= 0 || double.IsNaN(sec)) return "-";
        sec = Math.Round(sec, 1);
        int m = (int)(sec / 60);
        double s = sec - m * 60;
        var ss = s.ToString("00.0", Culture(lang));
        return m > 0 ? $"{m}:{ss}" : s.ToString("0.0", Culture(lang));
    }

    /// <summary>Rounded meters for tips (nearest 5 m, min 5).</summary>
    public static int RoundMeters(double m) => Math.Max(5, (int)(Math.Round(Math.Abs(m) / 5.0) * 5));
}
