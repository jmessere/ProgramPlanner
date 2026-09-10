namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// Deterministic name-to-color hex mapping shared by every template/phase
/// overlay in the app (Resource Plan grid overlay, Global Timeline overlay
/// and Template-grouped phase rows) so the same-named phase or template
/// always renders in the same color wherever it appears.
/// </summary>
public static class NameColorPalette
{
    private static readonly string[] Palette =
    {
        "#2b6cb0", "#c05621", "#2f855a", "#6b46c1", "#b7791f", "#c53030", "#2c7a7b", "#4a5568"
    };

    public static string ColorFor(string name)
    {
        var hash = 0;
        foreach (var c in name) hash = hash * 31 + c;
        return Palette[Math.Abs(hash) % Palette.Length];
    }
}
