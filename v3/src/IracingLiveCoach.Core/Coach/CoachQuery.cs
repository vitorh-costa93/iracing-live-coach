using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Everything <see cref="CoachQuery.Answer"/> needs (snapshot of the engine state).</summary>
public sealed record CoachQueryState(string Language, LapReport? LastReport, double? ReferenceLapSec,
    int SessionScore, string? Focus, string? LastMessageText, EngineerStatus? Engineer = null);

/// <summary>Builds messages from reports (shared by the engine and the query answers).</summary>
public static class CoachMessages
{
    public const int PriorityBriefing = 1;
    public const int PriorityPraise = 1;
    public const int PrioritySummary = 2;
    public const int PriorityTip = 3;
    public const int PriorityAnswer = 5;
    /// <summary>Engineer calls: normal (fuel info, gaps, position) and high (critical fuel, pit, contact). Flags and
    /// damage use <see cref="PriorityEngineerUrgent"/> and may interrupt speech.</summary>
    public const int PriorityEngineer = 4;
    public const int PriorityEngineerHigh = 5;
    public const int PriorityEngineerUrgent = 6;

    public static CoachMessage Build(string lang, CoachMessageKind kind, string key, Dictionary<string, string> args, int priority) =>
        new(kind, key, args, priority, CoachPhrases.Render(lang, key, args));

    /// <summary>Corner tip for the report's MainIssue, or null when the issue is None.</summary>
    public static CoachMessage? Tip(string lang, CornerReport c)
    {
        if (!Enum.TryParse<CornerIssue>(c.MainIssue, out var issue) || issue == CornerIssue.None) return null;
        var args = new Dictionary<string, string>
        {
            ["turn"] = c.Index.ToString(),
            ["m"] = CoachPhrases.RoundMeters(c.BrakePointDeltaM).ToString(),
            ["kmh"] = Math.Max(1, (int)Math.Round(Math.Abs(c.MinSpeedDeltaKmh))).ToString(),
            ["loss"] = CoachPhrases.Number(lang, c.TimeLostSec, 2),
        };
        return Build(lang, CoachMessageKind.CornerTip, "tip." + issue, args, CoachMessages.PriorityTip);
    }
}

/// <summary>Answers to voice/menu intents: "lastlap","worstcorner","score","focus","repeat","summary".</summary>
public static class CoachQuery
{
    public static readonly IReadOnlyList<string> Intents = new[] { "lastlap", "worstcorner", "score", "focus", "repeat", "summary",
        "fuel", "gapahead", "gapbehind", "position", "laps", "pit", "rejoin" };

    public static string NormalizeIntent(string? intent) =>
        new string((intent ?? "").ToLowerInvariant().Where(char.IsLetter).ToArray());

    public static string Answer(string intent, CoachQueryState st)
    {
        var lang = CoachPhrases.Normalize(st.Language);
        string R(string key, Dictionary<string, string>? a = null) => CoachPhrases.Render(lang, key, a);
        var last = st.LastReport;

        switch (NormalizeIntent(intent))
        {
            case "lastlap":
                if (last == null) return R("answer.nolap");
                return R("answer.lastlap", new()
                {
                    ["lap"] = CoachPhrases.LapTime(lang, last.LapTimeSec),
                    ["delta"] = CoachPhrases.Number(lang, Math.Max(0, last.DeltaToRefSec), 1),
                });

            case "worstcorner":
            {
                if (last == null) return R("answer.nolap");
                var worst = last.Corners.Where(c => c.MainIssue != nameof(CornerIssue.None))
                    .OrderByDescending(c => c.TimeLostSec).FirstOrDefault();
                if (worst == null) return R("answer.noloss");
                var tip = CoachMessages.Tip(lang, worst)?.Text ?? "";
                return R("answer.worstcorner", new()
                {
                    ["turn"] = worst.Index.ToString(),
                    ["loss"] = CoachPhrases.Number(lang, worst.TimeLostSec, 2),
                    ["tip"] = tip,
                }).Trim();
            }

            case "score":
                if (last == null || st.SessionScore <= 0) return R("answer.noscore");
                return R("answer.score", new()
                {
                    ["score"] = st.SessionScore.ToString(),
                    ["last"] = last.TechniqueScore.ToString(),
                });

            case "focus":
                if (string.IsNullOrEmpty(st.Focus)) return R("answer.nofocus");
                return R("answer.focus", new() { ["focus"] = R("focus." + st.Focus) });

            case "repeat":
                return string.IsNullOrEmpty(st.LastMessageText) ? R("answer.repeat.none") : st.LastMessageText;

            case "summary":
            {
                if (last == null) return R("answer.nolap");
                var sb = new StringBuilder(R("summary.head", new() { ["lap"] = CoachPhrases.LapTime(lang, last.LapTimeSec) }));
                var bad = last.Corners.Where(c => c.MainIssue != nameof(CornerIssue.None))
                    .OrderBy(c => c.Index).ToList();
                if (bad.Count == 0) sb.Append(' ').Append(R("summary.nocorner"));
                foreach (var c in bad)
                {
                    sb.Append(' ').Append(R("summary.corner", new()
                    {
                        ["turn"] = c.Index.ToString(),
                        ["loss"] = CoachPhrases.Number(lang, c.TimeLostSec, 2),
                        ["tip"] = CoachMessages.Tip(lang, c)?.Text ?? "",
                    }).Trim());
                }
                return sb.ToString();
            }

            case "fuel":
            {
                var e = st.Engineer;
                if (e == null || e.FuelL < 0) return R("answer.fuel.none");
                var a = new Dictionary<string, string> { ["fuel"] = CoachPhrases.Number(lang, e.FuelL, 1) };
                if (double.IsNaN(e.LapsLeft)) return R("answer.fuel.liters", a);
                a["n"] = ((int)Math.Floor(e.LapsLeft)).ToString();
                if (!double.IsNaN(e.ShortL) && e.ShortL > 0) { a["add"] = e.AddL.ToString(); return R("answer.fuel.add", a); }
                if (!double.IsNaN(e.ShortL)) return R("answer.fuel.enough", a);
                return R("answer.fuel", a);
            }

            case "gapahead":
                return st.Engineer is { } ga && !double.IsNaN(ga.GapAheadSec)
                    ? R("answer.gap.ahead", new() { ["gap"] = CoachPhrases.Number(lang, ga.GapAheadSec, 1) }) : R("answer.gap.none");

            case "gapbehind":
                return st.Engineer is { } gb && !double.IsNaN(gb.GapBehindSec)
                    ? R("answer.gap.behind", new() { ["gap"] = CoachPhrases.Number(lang, gb.GapBehindSec, 1) }) : R("answer.gap.none");

            case "position":
                return st.Engineer is { Position: > 0 } ep
                    ? R("answer.position", new() { ["pos"] = ep.Position.ToString() }) : R("answer.position.none");

            case "laps":
                return st.Engineer is { LapsToGo: > 0 } el
                    ? R("answer.laps", new() { ["n"] = el.LapsToGo.ToString() }) : R("answer.laps.none");

            case "rejoin":
            {
                if (st.Engineer?.Rejoin is not { Available: true } rj) return R("answer.rejoin.none");
                var (key, args) = RaceEngineer.RejoinPhrase(rj, lang);
                return R(key, args);
            }

            case "pit":
            {
                var e = st.Engineer;
                if (e == null || double.IsNaN(e.LapsLeft)) return R("answer.pit.none");
                if (e.BoxNow) return R("answer.pit.yes");
                return R("answer.pit.no", new() { ["n"] = ((int)Math.Floor(e.LapsLeft)).ToString() });
            }

            default:
                return R("answer.unknown");
        }
    }
}
