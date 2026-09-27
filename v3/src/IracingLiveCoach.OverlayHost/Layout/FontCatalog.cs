using Vortice.Win32;
using Vortice.Win32.Graphics.DirectWrite;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Layout;

/// <summary>The font families a widget can be set to (typography spec, phase 1). Barlow, Chakra
/// Petch, IBM Plex Sans and Inter come from the bundled private collection; SF Pro is NOT bundled
/// (Apple's licence forbids redistribution) -- it is looked up in the system collection and falls
/// back to Inter when not installed.</summary>
public static unsafe class FontCatalog
{
    public sealed record FamilyInfo(string Key, string Label, string DirectWriteName, bool Condensed, int[] Weights);

    public const string DefaultKey = "barlow";

    public static IReadOnlyList<FamilyInfo> Families { get; } =
    [
        new("barlow", "Barlow Semi Condensed", "Barlow", true, [400, 600]),
        new("chakra", "Chakra Petch", "Chakra Petch", false, [500, 600, 700]),
        new("plex", "IBM Plex Sans", "IBM Plex Sans", false, [400, 500, 600]),
        new("inter", "Inter", "Inter", false, [400]),
        // Only Regular/Medium/Bold are installed on this machine (SFPRODISPLAY{REGULAR,MEDIUM,BOLD}.OTF,
        // usWeightClass 400/500/700) -- no 600 cut exists, so it's not offered; requesting it would
        // silently snap to whichever of these three DirectWrite picks as nearest.
        new("sfpro", "SF Pro (instalada no Windows)", "SF Pro Display", false, [400, 500, 700]),
    ];

    public static FamilyInfo Get(string? key) =>
        Families.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Families[0];

    /// <summary>Weights the family really has, for the Control Center's weight list.</summary>
    public static IReadOnlyList<int> WeightsFor(string? key) => Get(key).Weights;

    private static bool? _sfProInstalled;

    private static bool IsSfProInstalled(IDWriteFactory* factory)
    {
        if (_sfProInstalled is bool cached) return cached;
        bool found = false;
        try
        {
            using ComPtr<IDWriteFontCollection> system = default;
            // checkForUpdates: true -- SF Pro is a per-user install (HKCU\...\Fonts, not HKLM), so the
            // first process to ask may need DirectWrite to rescan rather than trust an already-cached
            // system collection that predates the install.
            if (factory->GetSystemFontCollection(system.GetAddressOf(), true).Success)
            {
                fixed (char* p = "SF Pro Display")
                {
                    uint index = 0;
                    Bool32 exists = false;
                    system.Get()->FindFamilyName(p, &index, &exists);
                    found = exists;
                    if (exists)
                    {
                        using ComPtr<IDWriteFontFamily> fam = default;
                        if (system.Get()->GetFontFamily(index, fam.GetAddressOf()).Success)
                        {
                            uint faceCount = fam.Get()->GetFontCount();
                            var weights = new List<int>();
                            for (uint i = 0; i < faceCount; i++)
                            {
                                using ComPtr<IDWriteFont> face = default;
                                if (fam.Get()->GetFont(i, face.GetAddressOf()).Success)
                                    weights.Add((int)face.Get()->GetWeight());
                            }
                            Console.WriteLine($"[Fonts] SF Pro Display: {faceCount} face(s) in the system collection, weights [{string.Join(", ", weights)}].");
                        }
                    }
                }
            }
        }
        catch { found = false; }
        Console.WriteLine(found ? "[Fonts] SF Pro found in the system collection." : "[Fonts] SF Pro not installed -- falling back to Inter.");
        _sfProInstalled = found;
        return found;
    }

    private static int Snap(int[] available, int wanted)
    {
        int best = available[0];
        foreach (int w in available)
            if (Math.Abs(w - wanted) < Math.Abs(best - wanted)) best = w;
        return best;
    }

    /// <summary>Creates a text format for the appearance's family. <paramref name="forcedWeight"/> is
    /// <see cref="WidgetAppearance.FontWeight"/> (0 = keep <paramref name="ownWeight"/>, the widget's
    /// tuned weight); either is snapped to the nearest cut the family has.</summary>
    public static ComPtr<IDWriteTextFormat> CreateFormat(IDWriteFactory* factory, IDWriteFontCollection1* privateCollection,
        string? familyKey, float size, FontWeight ownWeight, int forcedWeight = 0)
    {
        var family = Get(familyKey);
        if (family.Key == "sfpro" && !IsSfProInstalled(factory)) family = Get("inter");

        int wanted = forcedWeight > 0 ? forcedWeight : (int)ownWeight;
        int weight = family.Key == "barlow" && forcedWeight <= 0 ? wanted : Snap(family.Weights, wanted);
        var collection = family.Key == "sfpro" ? null : (IDWriteFontCollection*)privateCollection;
        return factory->CreateTextFormat(family.DirectWriteName, collection, size, fontWeight: (FontWeight)weight,
            fontStretch: family.Condensed ? FontStretch.SemiCondensed : FontStretch.Normal, localeName: "en-us");
    }
}
