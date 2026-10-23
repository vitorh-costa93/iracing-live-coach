#!/usr/bin/env python3
"""Calibra o ritmo da IA do mod F1 2006 do AMS2 (UserData/CustomAIDrivers/F-V8_Gen1.xml).

Forca da equipe  = power/drag/weight_scalar, iguais para os pilotos da equipe.
Forca do piloto  = race_skill / qualifying_skill numa faixa estreita (0.92-1.00),
                   lider de cada equipe acima do(s) companheiro(s).
aggression       = valor atual do piloto remapeado linearmente para 0.60-0.90 (mantem o carater).
consistency      = 0.90-0.98, proporcional ao race_skill.

Blocos tracks="..." sao mantidos: setup_* e os demais campos ficam como estao; so os campos
calibrados recebem o mesmo valor do bloco base (e sao adicionados se faltarem), para que a ordem
nao mude a cada etapa e para funcionar tanto se o bloco por pista mescla quanto se substitui a base.

Edicao textual (preserva BOM, quebras de linha, comentarios e ordem). Valida antes de gravar:
XML bem formado, mesmas entradas (livery_name + tracks) na mesma ordem, nenhum campo perdido,
campos nao calibrados com valor identico, so campos calibrados adicionados, faixas e ordem interna.

Uso:
  python -I f1_2006_calibrate.py --input F-V8_Gen1.xml --output novo.xml [--report rel.txt]
  python -I f1_2006_calibrate.py --show F-V8_Gen1.xml      (tabela do bloco base)
Sai com codigo != 0 se a validacao falhar (nada e gravado nesse caso).
"""
from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET

# ---------------------------------------------------------------- tabela 2006
# Deficit alvo em % da volta por equipe (Renault = referencia). ~1.9 % = ~1.7 s numa volta de 90 s.
TEAM_DELTA_PCT = {
    "Renault": 0.00,
    "Ferrari": 0.05,
    "McLaren": 0.25,
    "Honda": 0.35,
    "BMW Sauber": 0.60,
    "Toyota": 0.60,
    "Red Bull": 0.85,
    "Williams": 0.85,
    "Toro Rosso": 1.10,
    "Midland": 1.45,
    "Super Aguri": 1.90,
}
# Modelo [hipotese, ajustar com quali so de IA]: dvolta% ~ 0.20*dmassa% + 0.15*dpot% + 0.15*darrasto%
# Divisao do deficit: 50 % peso, 30 % potencia, 20 % arrasto.
K_WEIGHT, K_POWER, K_DRAG = 0.025, 0.020, 0.0133

# livery_name -> (equipe, race_skill, delta quali). Somente pilotos que existem no XML do mod.
DRIVERS = {
    "Fernando Alonso #1":       ("Renault",     1.000,  0.000),
    "Giancarlo Fisichella #2":  ("Renault",     0.970,  0.005),
    "Michael Schumacher #5":    ("Ferrari",     1.000,  0.000),
    "Felipe Massa #6":          ("Ferrari",     0.980,  0.005),
    "Kimi Raikkonen #3":        ("McLaren",     0.990,  0.010),
    "Juan Pablo Montoya #4":    ("McLaren",     0.975, -0.010),
    "Pedro De La Rosa #4":      ("McLaren",     0.960, -0.005),
    "Jenson Button #12":        ("Honda",       0.980,  0.000),
    "Rubens Barrichello #11":   ("Honda",       0.970,  0.005),
    "Nick Heidfeld #16":        ("BMW Sauber",  0.970,  0.000),
    "Robert Kubica #17":        ("BMW Sauber",  0.965,  0.005),
    "Jacques Villeneuve #17":   ("BMW Sauber",  0.960, -0.005),
    "Ralf Schumacher #7":       ("Toyota",      0.965, -0.005),
    "Jarno Trulli #8":          ("Toyota",      0.960,  0.015),
    "Mark Webber #9":           ("Williams",    0.970,  0.005),
    "Nico Rosberg #10":         ("Williams",    0.960,  0.005),
    "David Coulthard #14":      ("Red Bull",    0.965, -0.010),
    "Christian Klien #15":      ("Red Bull",    0.950,  0.000),
    "Robert Doornbos #15":      ("Red Bull",    0.945,  0.000),
    "Vitantonio Liuzzi #20":    ("Toro Rosso",  0.950,  0.000),
    "Scott Speed #21":          ("Toro Rosso",  0.945, -0.005),
    "Christijan Albers #19":    ("Midland",     0.945,  0.005),
    "Tiago Monteiro #18":       ("Midland",     0.940, -0.005),
    "Takuma Sato #22":          ("Super Aguri", 0.950,  0.005),
    "Franck Montagny #23":      ("Super Aguri", 0.930,  0.000),
    "Sakon Yamamoto #23":       ("Super Aguri", 0.925,  0.000),
    "Yuji Ide #23":             ("Super Aguri", 0.920, -0.005),
}

AGGR_LO, AGGR_HI = 0.60, 0.90
CONS_LO, CONS_HI = 0.90, 0.98
SKILL_LO, SKILL_HI = 0.92, 1.00
SCALAR_LO, SCALAR_HI = 0.90, 1.10

CAL_TAGS = ("weight_scalar", "power_scalar", "drag_scalar", "race_skill", "qualifying_skill",
            "aggression", "consistency")
FMT = {"weight_scalar": "{:.3f}", "power_scalar": "{:.3f}", "drag_scalar": "{:.3f}",
       "race_skill": "{:.3f}", "qualifying_skill": "{:.3f}", "aggression": "{:.2f}",
       "consistency": "{:.2f}"}

COMMENT = ("<!-- Calib2006 (f1_2006_calibrate.py): equipe = power/drag/weight (ordem real 2006, ~1.9 % "
           "Renault->Super Aguri); piloto = race/qualifying_skill 0.92-1.00; blocos por pista com os "
           "mesmos valores calibrados (setup_* preservado) -->")

DRIVER_RE = re.compile(r"(<driver\b[^>]*>)(.*?)(</driver>)", re.S)
ATTR_RE = re.compile(r'(\w+)="([^"]*)"')


def tag_re(tag: str) -> re.Pattern:
    return re.compile(r"<%s>\s*([^<]*?)\s*</%s>" % (tag, tag))


def fields(body: str) -> dict:
    out = {}
    for m in re.finditer(r"<(\w+)>([^<]*)</\1>", body):
        if m.group(1) in out:
            raise ValueError("campo duplicado %s" % m.group(1))
        out[m.group(1)] = m.group(2).strip()
    return out


def parse_entries(text: str):
    ents = []
    for m in DRIVER_RE.finditer(text):
        attrs = dict(ATTR_RE.findall(m.group(1)))
        ents.append({"livery": attrs.get("livery_name"), "tracks": attrs.get("tracks", ""),
                     "fields": fields(m.group(2)), "span": m.span()})
    return ents


def team_scalars(team: str) -> dict:
    d = TEAM_DELTA_PCT[team]
    return {"weight_scalar": round(1 + K_WEIGHT * d, 3),
            "power_scalar": round(1 - K_POWER * d, 3),
            "drag_scalar": round(1 + K_DRAG * d, 3)}


def est_delta_pct(sc: dict) -> float:
    return (0.20 * (sc["weight_scalar"] - 1) + 0.15 * (1 - sc["power_scalar"])
            + 0.15 * (sc["drag_scalar"] - 1)) * 100


def build_targets(base: dict) -> dict:
    """base: livery -> campos do bloco base atual."""
    aggr_in = {k: float(v["aggression"]) for k, v in base.items() if "aggression" in v}
    a_min, a_max = min(aggr_in.values()), max(aggr_in.values())
    tg = {}
    for liv, (team, rs, dq) in DRIVERS.items():
        t = dict(team_scalars(team))
        t["race_skill"] = rs
        t["qualifying_skill"] = round(min(SKILL_HI, max(SKILL_LO, rs + dq)), 3)
        a = aggr_in.get(liv, (a_min + a_max) / 2)
        t["aggression"] = round(AGGR_LO + (a - a_min) / (a_max - a_min) * (AGGR_HI - AGGR_LO), 2)
        t["consistency"] = round(CONS_LO + (rs - SKILL_LO) / (SKILL_HI - SKILL_LO) * (CONS_HI - CONS_LO), 2)
        tg[liv] = t
    return tg


def apply_body(body: str, target: dict) -> str:
    nl = "\r\n" if "\r\n" in body else "\n"
    m = re.search(r"\n([ \t]+)<\w", body)
    indent = m.group(1) if m else "    "
    for tag in CAL_TAGS:
        val = FMT[tag].format(target[tag])
        rx = re.compile(r"(<%s>)[^<]*(</%s>)" % (tag, tag))
        n = len(rx.findall(body))
        if n > 1:
            raise ValueError("tag %s duplicada" % tag)
        if n == 1:
            body = rx.sub(lambda mm: mm.group(1) + val + mm.group(2), body)
        else:
            # insere antes do fechamento, mantendo a indentacao das outras tags
            stripped = body.rstrip(" \t")
            tail = body[len(stripped):]
            if not stripped.endswith("\n"):
                stripped += nl
            body = stripped + "%s<%s>%s</%s>%s" % (indent, tag, val, tag, nl) + tail
    return body


def calibrate(text: str):
    ents = parse_entries(text)
    base = {e["livery"]: e["fields"] for e in ents if not e["tracks"]}
    missing = set(base) - set(DRIVERS)
    invented = set(DRIVERS) - set(base)
    if missing or invented:
        raise ValueError("pilotos sem mapeamento: %s / mapeados que nao existem: %s"
                         % (sorted(missing), sorted(invented)))
    targets = build_targets(base)
    out, pos = [], 0
    for m in DRIVER_RE.finditer(text):
        attrs = dict(ATTR_RE.findall(m.group(1)))
        liv = attrs.get("livery_name")
        if liv not in targets:
            raise ValueError("bloco com piloto desconhecido: %s" % liv)
        out.append(text[pos:m.start()])
        out.append(m.group(1) + apply_body(m.group(2), targets[liv]) + m.group(3))
        pos = m.end()
    out.append(text[pos:])
    new = "".join(out)
    if "Calib2006" not in new:
        new = re.sub(r"(<custom_ai_drivers>)(\r?\n)",
                     lambda mm: mm.group(1) + mm.group(2) + "  " + COMMENT + mm.group(2), new, count=1)
    return new, targets


def validate(old: str, new: str, targets: dict) -> list:
    errs = []
    try:
        ET.fromstring(new.encode("utf-8"))
    except ET.ParseError as ex:
        errs.append("XML mal formado: %s" % ex)
        return errs
    eo, en = parse_entries(old), parse_entries(new)
    if [(e["livery"], e["tracks"]) for e in eo] != [(e["livery"], e["tracks"]) for e in en]:
        errs.append("entradas (livery_name/tracks) diferentes ou fora de ordem")
        return errs
    for a, b in zip(eo, en):
        tag = "%s [%s]" % (a["livery"], a["tracks"][:30] or "base")
        lost = set(a["fields"]) - set(b["fields"])
        added = set(b["fields"]) - set(a["fields"])
        if lost:
            errs.append("%s: campos perdidos %s" % (tag, sorted(lost)))
        if added - set(CAL_TAGS):
            errs.append("%s: campos estranhos adicionados %s" % (tag, sorted(added - set(CAL_TAGS))))
        for k, v in a["fields"].items():
            if k not in CAL_TAGS and b["fields"].get(k) != v:
                errs.append("%s: campo nao calibrado alterado %s %s->%s" % (tag, k, v, b["fields"].get(k)))
        for k in CAL_TAGS:
            if k not in b["fields"] or abs(float(b["fields"][k]) - targets[a["livery"]][k]) > 1e-9:
                errs.append("%s: %s nao bate com o alvo" % (tag, k))
    # texto fora dos blocos <driver> identico (exceto o comentario adicionado)
    strip = lambda s: DRIVER_RE.sub("", s.replace("  " + COMMENT, "")).replace("\r\n", "\n")
    if re.sub(r"\n\s*\n", "\n", strip(old)) != re.sub(r"\n\s*\n", "\n", strip(new)):
        errs.append("texto fora dos blocos <driver> mudou")
    # faixas e ordem interna
    by_team = {}
    for liv, t in targets.items():
        for k in ("weight_scalar", "power_scalar", "drag_scalar"):
            if not SCALAR_LO <= t[k] <= SCALAR_HI:
                errs.append("%s: %s fora de 0.9-1.1" % (liv, k))
        for k in ("race_skill", "qualifying_skill"):
            if not SKILL_LO <= t[k] <= SKILL_HI:
                errs.append("%s: %s fora de %.2f-%.2f" % (liv, k, SKILL_LO, SKILL_HI))
        by_team.setdefault(DRIVERS[liv][0], []).append(t["race_skill"])
    for team, rs in by_team.items():
        s = sorted(rs, reverse=True)
        if len(s) > 1 and s[0] <= s[1]:
            errs.append("%s: lider nao esta acima do companheiro" % team)
    return errs


def table(text: str) -> str:
    ents = [e for e in parse_entries(text) if not e["tracks"]]
    cols = ("weight_scalar", "power_scalar", "drag_scalar", "race_skill", "qualifying_skill",
            "aggression", "consistency")
    lines = ["%-26s %-11s %6s %6s %6s %6s %6s %5s %5s  est_s@90" % (
        "livery", "team", "weight", "power", "drag", "race", "quali", "aggr", "cons")]
    for e in ents:
        f = e["fields"]
        team = DRIVERS.get(e["livery"], ("?",))[0]
        try:
            sc = {k: float(f[k]) for k in ("weight_scalar", "power_scalar", "drag_scalar")}
            est = "%.2f" % (est_delta_pct(sc) * 0.9)
        except (KeyError, ValueError):
            est = "-"
        lines.append("%-26s %-11s %6s %6s %6s %6s %6s %5s %5s  %s" % (
            e["livery"], team, *[f.get(c, "-") for c in cols], est))
    return "\n".join(lines)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--input")
    ap.add_argument("--output")
    ap.add_argument("--report")
    ap.add_argument("--show")
    a = ap.parse_args(argv)
    if a.show:
        with open(a.show, "rb") as fh:
            print(table(fh.read().decode("utf-8-sig")))
        return 0
    if not (a.input and a.output):
        ap.error("--input e --output sao obrigatorios")
    raw = open(a.input, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    old = raw.decode("utf-8-sig")
    new, targets = calibrate(old)
    errs = validate(old, new, targets)
    if errs:
        print("VALIDACAO FALHOU (nada gravado):", file=sys.stderr)
        for e in errs[:50]:
            print("  " + e, file=sys.stderr)
        return 2
    with open(a.output, "wb") as fh:
        fh.write((b"\xef\xbb\xbf" if bom else b"") + new.encode("utf-8"))
    rep = "OK: %d entradas validadas\n\n%s\n" % (len(parse_entries(new)), table(new))
    if a.report:
        with open(a.report, "w", encoding="utf-8") as fh:
            fh.write(rep)
    print(rep)
    return 0


if __name__ == "__main__":
    sys.exit(main())
