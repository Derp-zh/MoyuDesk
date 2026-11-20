namespace Taskboard.Core;

public static class OptionPalette
{
    public const string Codex = "#C1A0EB";
    public const string DeepSeek = "#82AFF7";
    public const string WorkBuddy = "#77CBB0";
    public const string Claude = "#FFA773";

    // Only exact former defaults are upgraded. IDs, renamed/deleted options and
    // user-defined colors survive. The persisted marker also respects later deletion.
    public static void Upgrade(BoardState state)
    {
        if (state.Preferences.PlatformPaletteVersion >= 1) return;
        foreach (var option in state.AiOptions)
        {
            if (option.Name.Equals("codex", StringComparison.OrdinalIgnoreCase) && option.Color.Equals("#77CBB0", StringComparison.OrdinalIgnoreCase)) option.Color = Codex;
            if (option.Name.Equals("workbuddy", StringComparison.OrdinalIgnoreCase) && option.Color.Equals("#C1A0EB", StringComparison.OrdinalIgnoreCase)) option.Color = WorkBuddy;
        }
        if (!state.AiOptions.Any(x => x.Name.Equals("Claude", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("Claude Code", StringComparison.OrdinalIgnoreCase)))
            state.AiOptions.Add(new FieldOption { Name = "Claude", Color = Claude });
        state.Preferences.PlatformPaletteVersion = 1;
    }
}
