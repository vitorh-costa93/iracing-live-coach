namespace Ams2.Shared.Liveries;

/// <summary>Equipe de cada piloto na temporada 2006 do mod F1 2006: as pinturas dele so citam o piloto ("Christian Klien #15").</summary>
static class ClassicTeams
{
    static readonly Dictionary<string, string> Teams2006 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Christian Klien"] = "Red Bull", ["David Coulthard"] = "Red Bull", ["Robert Doornbos"] = "Red Bull",
        ["Christijan Albers"] = "Midland", ["Tiago Monteiro"] = "Midland",
        ["Felipe Massa"] = "Ferrari", ["Michael Schumacher"] = "Ferrari",
        ["Fernando Alonso"] = "Renault", ["Giancarlo Fisichella"] = "Renault",
        ["Franck Montagny"] = "Super Aguri", ["Sakon Yamamoto"] = "Super Aguri", ["Takuma Sato"] = "Super Aguri", ["Yuji Ide"] = "Super Aguri",
        ["Jacques Villeneuve"] = "BMW Sauber", ["Nick Heidfeld"] = "BMW Sauber", ["Robert Kubica"] = "BMW Sauber",
        ["Jarno Trulli"] = "Toyota", ["Ralf Schumacher"] = "Toyota",
        ["Jenson Button"] = "Honda", ["Rubens Barrichello"] = "Honda",
        ["Juan-Pablo Montoya"] = "McLaren", ["Kimi Räikkönen"] = "McLaren", ["Pedro de la Rosa"] = "McLaren",
        ["Mark Webber"] = "Williams", ["Nico Rosberg"] = "Williams",
        ["Scott Speed"] = "Toro Rosso", ["Vitantonio Liuzzi"] = "Toro Rosso",
    };

    public static string? Of2006(string driver) => Teams2006.TryGetValue(driver.Trim(), out var t) ? t : null;
}
