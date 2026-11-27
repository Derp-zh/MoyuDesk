using System.Text.Json;

namespace Taskboard.Core;

public enum Lane { Inspiration, Refining, Running, Action, Paused, Done }
public enum Repeat { Once, Daily, Weekly }
public enum NewStageDefaultState { ContextDependent, AlwaysRunning, AlwaysInactive }
public enum ThemeMode { Dark, Light, System }

public static class ThemePresets
{
    public const string DefaultDark = "midnight-cyan";
    public const string DefaultLight = "daylight-mist";

    public static readonly string[] DarkIds =
    [
        "midnight-cyan",
        "obsidian-amber",
        "forest-jade",
        "nebula-violet",
        "graphite-coral"
    ];

    public static readonly string[] LightIds =
    [
        "daylight-mist",
        "warm-paper",
        "mint-breeze",
        "lavender-day"
    ];

    public static bool IsValidDark(string? id) =>
        !string.IsNullOrWhiteSpace(id) && DarkIds.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));

    public static bool IsValidLight(string? id) =>
        !string.IsNullOrWhiteSpace(id) && LightIds.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));

    public static string NormalizeDark(string? id) =>
        DarkIds.FirstOrDefault(x => string.Equals(x, id?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? DefaultDark;

    public static string NormalizeLight(string? id) =>
        LightIds.FirstOrDefault(x => string.Equals(x, id?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? DefaultLight;

    public static ThemeMode NormalizeMode(ThemeMode mode) =>
        Enum.IsDefined(mode) ? mode : ThemeMode.Dark;
}

public static class Names
{
    public static readonly string[] Lanes = ["灵感库", "完善中", "进行中", "待操作", "暂搁置", "已完成"];
}

public sealed class Stage
{
    // Null only occurs in legacy data; WorkItem maps the former current stage once.
    public bool? IsRunning { get; set; }
    public CodexBinding? Codex { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    [System.Text.Json.Serialization.JsonRequired]
    public int Number { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public string Name { get; set; } = "阶段0";
    [System.Text.Json.Serialization.JsonRequired]
    public string Text { get; set; } = "";
}

public sealed class Reminder
{
    public static DateTime DefaultNextLocal(DateTime now) =>
        new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);

    public Reminder()
    {
        var initial = DefaultNextLocal(DateTime.Now);
        NextLocal = initial;
        Weekday = initial.DayOfWeek;
        Time = initial.TimeOfDay;
    }

    [System.Text.Json.Serialization.JsonRequired]
    public bool Enabled { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public Repeat Repeat { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public DateTime NextLocal { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public DayOfWeek Weekday { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public TimeSpan Time { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public DateTime? LastOccurrence { get; set; }
}

public sealed class WorkItem : System.Text.Json.Serialization.IJsonOnDeserialized
{
    public DateTime? CompletedSinceUtc { get; set; }
    public Guid? ColumnId { get; set; }
    // Read legacy task bindings once, then persist bindings on their owning stage.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CodexBinding? Codex { get; set; }
    public bool TimerUnread { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    [System.Text.Json.Serialization.JsonRequired]
    public string Title { get; set; } = "";
    [System.Text.Json.Serialization.JsonRequired]
    public Lane Lane { get; set; } = Lane.Refining;
    [System.Text.Json.Serialization.JsonRequired]
    public List<Stage> Stages { get; set; } = [new()];
    [System.Text.Json.Serialization.JsonRequired]
    // Legacy location retained for migration; display and automation use IsRunning.
    public Guid CurrentStageId { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public List<Guid> AiIds { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public Guid? PriorityId { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public Reminder Reminder { get; set; } = new();
    [System.Text.Json.Serialization.JsonRequired]
    public List<string> PendingReasons { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public WorkItem() { CurrentStageId = Stages[0].Id; Stages[0].IsRunning = true; }
    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
    {
        if (Stages is not null) foreach (var stage in Stages.Where(x => x is not null)) stage.IsRunning ??= stage.Id == CurrentStageId;
        if (Stages is { Count: > 0 } && !Stages.Any(x => x?.IsRunning == true) &&
            (Stages.FirstOrDefault(x => x?.Id == CurrentStageId) ?? Stages.FirstOrDefault(x => x is not null)) is { } fallback)
            fallback.IsRunning = true;
        if (Codex is null) return;
        var current = Stages?.FirstOrDefault(x => x?.Id == CurrentStageId);
        if (current is null) throw new JsonException("旧会话关联缺少对应的当前阶段");
        if (current.Codex is not null && !Copies.Same(current.Codex, Codex)) throw new JsonException("阶段与旧任务会话关联冲突");
        current.Codex = Codex; Codex = null;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    // Compatibility accessor; new UI and observers must address stages individually.
    public CodexBinding? CurrentCodex => Stages.FirstOrDefault(x => x.Id == CurrentStageId)?.Codex;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasContent => !string.IsNullOrWhiteSpace(Title) || Stages.Any(x => !string.IsNullOrWhiteSpace(x.Text));
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title.Trim();
            foreach (var stage in Stages)
            {
                if (Stages.Count != 1 && stage.IsRunning != true) continue;
                var line = FirstNonEmptyLine(stage.Text);
                if (line is not null) return line;
            }
            return "未命名任务";
        }
    }
    private static string? FirstNonEmptyLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var index = 0;
        while (index < text.Length)
        {
            var end = text.IndexOfAny(['\r', '\n'], index);
            var length = (end < 0 ? text.Length : end) - index;
            if (length > 0)
            {
                var slice = text.AsSpan(index, length).Trim();
                if (!slice.IsEmpty) return slice.ToString();
            }
            if (end < 0) break;
            index = end + 1;
        }
        return null;
    }
}

public sealed class EditSession
{
    // Older sessions already have a list; the shared host's new entry starts unassigned.
    public bool StatusSelected { get; set; } = true;
    public DateTime? CreatedUtc { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime CreationTime => CreatedUtc ?? Original.UpdatedUtc;
    [System.Text.Json.Serialization.JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    [System.Text.Json.Serialization.JsonRequired]
    public Guid? TaskId { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public WorkItem Original { get; set; } = new();
    [System.Text.Json.Serialization.JsonRequired]
    public WorkItem Working { get; set; } = new();
    [System.Text.Json.Serialization.JsonRequired]
    public bool Visible { get; set; } = true;
    [System.Text.Json.Serialization.JsonRequired]
    public bool Inline { get; set; }
}

public sealed class FieldOption
{
    [System.Text.Json.Serialization.JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    [System.Text.Json.Serialization.JsonRequired]
    public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonRequired]
    public string Color { get; set; } = "#8B9BB4";
}

public sealed class WindowPosition
{
    [System.Text.Json.Serialization.JsonRequired]
    public double X { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public double Y { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public double Width { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public double Height { get; set; }
}

public sealed class Preferences : System.Text.Json.Serialization.IJsonOnDeserialized
{
    // Legacy preference is retained for old backups; no longer starts notification capture.
    public bool CodexNotificationWaiting { get; set; }
    public bool CodexBehaviorSupplement { get; set; } = true;
    public bool AutoCreateAiTasks { get; set; }
    public bool QuickAiOnAllColumns { get; set; }
    public bool CompletedChronological { get; set; } = true;
    public bool RunningFocusMode { get; set; }
    public bool AutoOpenConversationBindingWindow { get; set; } = true;
    public bool EllipsizeStageDetails { get; set; } = true;
    public List<string> ExpandedStageDetails { get; set; } = [];
    public bool CollapseInactiveStageDetails { get; set; } = true;
    public NewStageDefaultState NewStageDefaultState { get; set; } = NewStageDefaultState.ContextDependent;
    public int PlatformPaletteVersion { get; set; }
    public List<string> RecentColors { get; set; } = [];
    public bool AutoArchiveCompleted { get; set; } = true;
    public int AutoArchiveAfterDays { get; set; } = 5;
    public bool AutoDeleteArchives { get; set; }
    public bool InspirationQuickEntry { get; set; } = true;
    public bool BallEdgeAutoHide { get; set; } = true;
    public BoardHotkey Hotkey { get; set; } = new();
    public NotificationListeningOptions Listening { get; set; } = new();
    public List<BoardColumn> Columns { get; set; } = BoardColumn.Defaults();
    // Optional for backwards compatibility with 0.3/0.4 data files.
    public double GlassTransparency { get; set; } = 20;
    public double FloatingTransparency { get; set; } = 10;
    public double BallTransparency { get; set; } = 40;
    public double PinnedActionTransparency { get; set; } = 70;
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;
    public string DarkThemePreset { get; set; } = ThemePresets.DefaultDark;
    public string LightThemePreset { get; set; } = ThemePresets.DefaultLight;
    [System.Text.Json.Serialization.JsonRequired]
    public bool ResizeCorner { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public bool BallVisible { get; set; } = true;
    [System.Text.Json.Serialization.JsonRequired]
    public bool AutoStart { get; set; } = true;
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, WindowPosition> Positions { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, string> ConnectionConfigs { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, string> MachineMarkers { get; set; } = [];
    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
    {
        Validation.Normalize(this);
        if (Columns is null) return;
        // These three built-ins could be deleted in previous versions. Preserve
        // the saved identity/name and the user's choice not to show the window.
        foreach (var lane in new[] { Lane.Refining, Lane.Paused, Lane.Done })
        {
            var column = Columns.FirstOrDefault(x => x.BuiltinLane == lane);
            if (column is null) { column = BoardColumn.Defaults().Single(x => x.BuiltinLane == lane); column.Visible = false; Columns.Add(column); }
            else if (column.Deleted) { column.Deleted = false; column.Visible = false; }
        }
    }
}

public sealed class ArchivedTask
{
    public WorkItem Item { get; set; } = new();
    public BoardColumn OriginalColumn { get; set; } = new();
    public BoardColumn? RestoreColumn { get; set; }
    public DateTime ArchivedUtc { get; set; } = DateTime.UtcNow;
    public int OriginalIndex { get; set; }
}

public sealed class BoardState
{
    public Dictionary<string, string> AiCreationCursors { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public int Version { get; set; } = 2;
    public Guid PersistenceRevision { get; set; }
    public List<ArchivedTask> Archives { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public List<WorkItem> Tasks { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public List<EditSession> Sessions { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public List<FieldOption> AiOptions { get; set; } =
    [new() { Name = "codex", Color = OptionPalette.Codex }, new() { Name = "dsh", Color = OptionPalette.DeepSeek }, new() { Name = "workbuddy", Color = OptionPalette.WorkBuddy }, new() { Name = "Claude", Color = OptionPalette.Claude }];
    [System.Text.Json.Serialization.JsonRequired]
    public List<FieldOption> PriorityOptions { get; set; } =
    [new() { Name = "紧急", Color = "#F28F98" }, new() { Name = "重要不紧急", Color = "#E9C582" }, new() { Name = "不重要不紧急", Color = "#93A6BE" }];
    [System.Text.Json.Serialization.JsonRequired]
    public Preferences Preferences { get; set; } = new();
}

public static class Copies
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNameCaseInsensitive = false };
    public static T Of<T>(T value)
    {
        if (value is null) return value;
        if (typeof(T) == typeof(WorkItem)) return (T)(object)CloneWorkItem((WorkItem)(object)value);
        if (typeof(T) == typeof(Stage)) return (T)(object)CloneStage((Stage)(object)value);
        if (typeof(T) == typeof(EditSession)) return (T)(object)CloneSession((EditSession)(object)value);
        if (typeof(T) == typeof(BoardState)) return (T)(object)CloneState((BoardState)(object)value);
        if (typeof(T) == typeof(Reminder)) return (T)(object)CloneReminder((Reminder)(object)value);
        if (typeof(T) == typeof(CodexBinding)) return (T)(object)CloneBinding((CodexBinding)(object)value);
        if (typeof(T) == typeof(WindowPosition)) return (T)(object)ClonePosition((WindowPosition)(object)value);
        if (typeof(T) == typeof(Preferences)) return (T)(object)ClonePreferences((Preferences)(object)value);
        if (typeof(T) == typeof(BoardColumn)) return (T)(object)CloneColumn((BoardColumn)(object)value);
        if (typeof(T) == typeof(ArchivedTask)) return (T)(object)CloneArchive((ArchivedTask)(object)value);
        if (typeof(T) == typeof(BoardHotkey)) return (T)(object)CloneHotkey((BoardHotkey)(object)value);
        if (typeof(T) == typeof(FieldOption)) return (T)(object)CloneOption((FieldOption)(object)value);
        if (typeof(T) == typeof(Dictionary<string, WindowPosition>)) return (T)(object)ClonePositions((Dictionary<string, WindowPosition>)(object)value);
        return JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), Json)!;
    }

    public static bool Same<T>(T a, T b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (typeof(T) == typeof(WorkItem)) return SameWorkItem((WorkItem)(object)a, (WorkItem)(object)b);
        if (typeof(T) == typeof(List<Guid>)) return SameGuidList((List<Guid>)(object)a, (List<Guid>)(object)b);
        if (typeof(T) == typeof(CodexBinding)) return SameBinding((CodexBinding)(object)a, (CodexBinding)(object)b);
        return JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
    }

    private static WorkItem CloneWorkItem(WorkItem item)
    {
        var copy = new WorkItem
        {
            CompletedSinceUtc = item.CompletedSinceUtc,
            ColumnId = item.ColumnId,
            Codex = item.Codex is null ? null : CloneBinding(item.Codex),
            TimerUnread = item.TimerUnread,
            Id = item.Id,
            Title = item.Title,
            Lane = item.Lane,
            Stages = item.Stages.Select(CloneStage).ToList(),
            CurrentStageId = item.CurrentStageId,
            AiIds = [.. item.AiIds],
            PriorityId = item.PriorityId,
            Reminder = CloneReminder(item.Reminder),
            PendingReasons = [.. item.PendingReasons],
            UpdatedUtc = item.UpdatedUtc
        };
        if (copy.Stages.Count > 0 && !copy.Stages.Any(x => x.IsRunning == true)
            && (copy.Stages.FirstOrDefault(x => x.Id == copy.CurrentStageId) ?? copy.Stages[0]) is { } fallback)
            fallback.IsRunning = true;
        return copy;
    }

    private static Stage CloneStage(Stage stage) => new()
    {
        IsRunning = stage.IsRunning,
        Codex = stage.Codex is null ? null : CloneBinding(stage.Codex),
        Id = stage.Id,
        Number = stage.Number,
        Name = stage.Name,
        Text = stage.Text
    };

    private static CodexBinding CloneBinding(CodexBinding b) => new()
    {
        Platform = b.Platform,
        BoundAtUtc = b.BoundAtUtc,
        ExternalTurnStartedUtc = b.ExternalTurnStartedUtc,
        BindingId = b.BindingId,
        ThreadId = b.ThreadId,
        ThreadName = b.ThreadName,
        RelatedSources = b.RelatedSources.Select(CloneBinding).ToList(),
        Generation = b.Generation,
        Phase = b.Phase,
        AwaitingUserAction = b.AwaitingUserAction,
        SidebarWaitingSinceUnixMs = b.SidebarWaitingSinceUnixMs,
        SidebarWaitIdentity = b.SidebarWaitIdentity,
        ActionTokens = [.. b.ActionTokens],
        ActionScope = b.ActionScope,
        ActionCycle = b.ActionCycle,
        AcknowledgedActionTokens = [.. b.AcknowledgedActionTokens],
        AcknowledgedActionScope = b.AcknowledgedActionScope,
        AcknowledgedAtUnixMs = b.AcknowledgedAtUnixMs,
        TurnId = b.TurnId,
        SourceEpoch = b.SourceEpoch,
        LastSequence = b.LastSequence,
        PendingRequests = [.. b.PendingRequests],
        PendingApprovals = [.. b.PendingApprovals],
        PendingRequestOverflow = b.PendingRequestOverflow,
        PreviousThreads = [.. b.PreviousThreads]
    };

    private static Reminder CloneReminder(Reminder r) => new()
    {
        Enabled = r.Enabled,
        Repeat = r.Repeat,
        NextLocal = r.NextLocal,
        Weekday = r.Weekday,
        Time = r.Time,
        LastOccurrence = r.LastOccurrence
    };

    private static EditSession CloneSession(EditSession s) => new()
    {
        StatusSelected = s.StatusSelected,
        CreatedUtc = s.CreatedUtc,
        Id = s.Id,
        TaskId = s.TaskId,
        Original = CloneWorkItem(s.Original),
        Working = CloneWorkItem(s.Working),
        Visible = s.Visible,
        Inline = s.Inline
    };

    private static ArchivedTask CloneArchive(ArchivedTask a) => new()
    {
        Item = CloneWorkItem(a.Item),
        OriginalColumn = CloneColumn(a.OriginalColumn),
        RestoreColumn = a.RestoreColumn is null ? null : CloneColumn(a.RestoreColumn),
        ArchivedUtc = a.ArchivedUtc,
        OriginalIndex = a.OriginalIndex
    };

    private static BoardColumn CloneColumn(BoardColumn c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        BuiltinLane = c.BuiltinLane,
        Visible = c.Visible,
        Deleted = c.Deleted
    };

    private static WindowPosition ClonePosition(WindowPosition p) => new()
    {
        X = p.X,
        Y = p.Y,
        Width = p.Width,
        Height = p.Height
    };

    private static Dictionary<string, WindowPosition> ClonePositions(Dictionary<string, WindowPosition> positions)
    {
        var copy = new Dictionary<string, WindowPosition>(positions.Count, positions.Comparer);
        foreach (var pair in positions) copy[pair.Key] = ClonePosition(pair.Value);
        return copy;
    }

    private static BoardHotkey CloneHotkey(BoardHotkey h) => new()
    {
        Enabled = h.Enabled,
        Modifiers = h.Modifiers,
        VirtualKey = h.VirtualKey
    };

    private static FieldOption CloneOption(FieldOption o) => new()
    {
        Id = o.Id,
        Name = o.Name,
        Color = o.Color
    };

    private static NotificationListeningOptions CloneListening(NotificationListeningOptions l) => new()
    {
        Enabled = l.Enabled,
        CaptureMode = l.CaptureMode,
        Volume = l.Volume,
        Rules = l.Rules.Select(r => new NotificationSoundRule
        {
            Id = r.Id,
            AppName = r.AppName,
            AppId = r.AppId,
            Enabled = r.Enabled,
            AudioPath = r.AudioPath
        }).ToList()
    };

    private static Preferences ClonePreferences(Preferences p)
    {
        var copy = new Preferences
        {
            CodexNotificationWaiting = p.CodexNotificationWaiting,
            CodexBehaviorSupplement = p.CodexBehaviorSupplement,
            AutoCreateAiTasks = p.AutoCreateAiTasks,
            QuickAiOnAllColumns = p.QuickAiOnAllColumns,
            CompletedChronological = p.CompletedChronological,
            RunningFocusMode = p.RunningFocusMode,
            AutoOpenConversationBindingWindow = p.AutoOpenConversationBindingWindow,
            EllipsizeStageDetails = p.EllipsizeStageDetails,
            ExpandedStageDetails = [.. p.ExpandedStageDetails],
            CollapseInactiveStageDetails = p.CollapseInactiveStageDetails,
            NewStageDefaultState = p.NewStageDefaultState,
            PlatformPaletteVersion = p.PlatformPaletteVersion,
            RecentColors = [.. p.RecentColors],
            AutoArchiveCompleted = p.AutoArchiveCompleted,
            AutoArchiveAfterDays = p.AutoArchiveAfterDays,
            AutoDeleteArchives = p.AutoDeleteArchives,
            InspirationQuickEntry = p.InspirationQuickEntry,
            BallEdgeAutoHide = p.BallEdgeAutoHide,
            Hotkey = CloneHotkey(p.Hotkey),
            Listening = CloneListening(p.Listening),
            Columns = p.Columns.Select(CloneColumn).ToList(),
            GlassTransparency = p.GlassTransparency,
            FloatingTransparency = p.FloatingTransparency,
            BallTransparency = p.BallTransparency,
            PinnedActionTransparency = p.PinnedActionTransparency,
            ThemeMode = p.ThemeMode,
            DarkThemePreset = p.DarkThemePreset,
            LightThemePreset = p.LightThemePreset,
            ResizeCorner = p.ResizeCorner,
            BallVisible = p.BallVisible,
            AutoStart = p.AutoStart,
            Positions = ClonePositions(p.Positions),
            ConnectionConfigs = new Dictionary<string, string>(p.ConnectionConfigs, p.ConnectionConfigs.Comparer),
            MachineMarkers = new Dictionary<string, string>(p.MachineMarkers, p.MachineMarkers.Comparer)
        };
        foreach (var lane in new[] { Lane.Refining, Lane.Paused, Lane.Done })
        {
            var column = copy.Columns.FirstOrDefault(x => x.BuiltinLane == lane);
            if (column is null) { column = BoardColumn.Defaults().Single(x => x.BuiltinLane == lane); column.Visible = false; copy.Columns.Add(column); }
            else if (column.Deleted) { column.Deleted = false; column.Visible = false; }
        }
        return copy;
    }

    private static BoardState CloneState(BoardState s) => new()
    {
        AiCreationCursors = new Dictionary<string, string>(s.AiCreationCursors, s.AiCreationCursors.Comparer),
        Version = s.Version,
        PersistenceRevision = s.PersistenceRevision,
        Archives = s.Archives.Select(CloneArchive).ToList(),
        Tasks = s.Tasks.Select(CloneWorkItem).ToList(),
        Sessions = s.Sessions.Select(CloneSession).ToList(),
        AiOptions = s.AiOptions.Select(CloneOption).ToList(),
        PriorityOptions = s.PriorityOptions.Select(CloneOption).ToList(),
        Preferences = ClonePreferences(s.Preferences)
    };

    private static bool SameGuidList(List<Guid> a, List<Guid> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static bool SameStringList(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool SameWorkItem(WorkItem a, WorkItem b)
    {
        if (a.Id != b.Id || a.ColumnId != b.ColumnId || a.Lane != b.Lane || a.TimerUnread != b.TimerUnread
            || a.CompletedSinceUtc != b.CompletedSinceUtc || a.CurrentStageId != b.CurrentStageId
            || a.PriorityId != b.PriorityId || a.UpdatedUtc != b.UpdatedUtc
            || !string.Equals(a.Title, b.Title, StringComparison.Ordinal)
            || !SameGuidList(a.AiIds, b.AiIds)
            || !SameStringList(a.PendingReasons, b.PendingReasons)
            || !SameReminder(a.Reminder, b.Reminder)
            || !SameBinding(a.Codex, b.Codex)
            || a.Stages.Count != b.Stages.Count) return false;
        for (var i = 0; i < a.Stages.Count; i++)
        {
            var sa = a.Stages[i]; var sb = b.Stages[i];
            if (sa.Id != sb.Id || sa.Number != sb.Number || sa.IsRunning != sb.IsRunning
                || !string.Equals(sa.Name, sb.Name, StringComparison.Ordinal)
                || !string.Equals(sa.Text, sb.Text, StringComparison.Ordinal)
                || !SameBinding(sa.Codex, sb.Codex)) return false;
        }
        return true;
    }

    private static bool SameReminder(Reminder a, Reminder b) =>
        a.Enabled == b.Enabled && a.Repeat == b.Repeat && a.NextLocal == b.NextLocal
        && a.Weekday == b.Weekday && a.Time == b.Time && a.LastOccurrence == b.LastOccurrence;

    private static bool SameBinding(CodexBinding? a, CodexBinding? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Platform != b.Platform || a.BoundAtUtc != b.BoundAtUtc || a.ExternalTurnStartedUtc != b.ExternalTurnStartedUtc
            || a.BindingId != b.BindingId || !string.Equals(a.ThreadId, b.ThreadId, StringComparison.Ordinal)
            || !string.Equals(a.ThreadName, b.ThreadName, StringComparison.Ordinal) || a.Generation != b.Generation
            || a.Phase != b.Phase || a.AwaitingUserAction != b.AwaitingUserAction
            || a.SidebarWaitingSinceUnixMs != b.SidebarWaitingSinceUnixMs
            || !string.Equals(a.SidebarWaitIdentity, b.SidebarWaitIdentity, StringComparison.Ordinal)
            || !SameStringList(a.ActionTokens, b.ActionTokens)
            || !string.Equals(a.ActionScope, b.ActionScope, StringComparison.Ordinal)
            || a.ActionCycle != b.ActionCycle
            || !SameStringList(a.AcknowledgedActionTokens, b.AcknowledgedActionTokens)
            || !string.Equals(a.AcknowledgedActionScope, b.AcknowledgedActionScope, StringComparison.Ordinal)
            || a.AcknowledgedAtUnixMs != b.AcknowledgedAtUnixMs
            || !string.Equals(a.TurnId, b.TurnId, StringComparison.Ordinal)
            || !string.Equals(a.SourceEpoch, b.SourceEpoch, StringComparison.Ordinal)
            || a.LastSequence != b.LastSequence
            || !SameStringList(a.PendingRequests, b.PendingRequests)
            || !SameStringList(a.PendingApprovals, b.PendingApprovals)
            || a.PendingRequestOverflow != b.PendingRequestOverflow
            || !SameStringList(a.PreviousThreads, b.PreviousThreads)
            || a.RelatedSources.Count != b.RelatedSources.Count) return false;
        for (var i = 0; i < a.RelatedSources.Count; i++)
            if (!SameBinding(a.RelatedSources[i], b.RelatedSources[i])) return false;
        return true;
    }
}
