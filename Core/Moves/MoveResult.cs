namespace Core;

// Error = move refused. Warning = move allowed but leaving someone in debt or below 0 VP (refused in Strict).
// Note = move allowed, with something worth knowing (spades lost, a wasted power action, ...).
public enum Severity { Note, Warning, Error }

public sealed record Issue(Severity Severity, string Message);

public sealed record MoveResult(
    GameState State,
    IReadOnlyList<GameEvent> Events,
    IReadOnlyList<Issue> Issues)
{
    public static readonly IReadOnlyList<Issue> NoIssues = Array.Empty<Issue>();

    public bool Accepted => Issues.All(issue => issue.Severity != Severity.Error);

    public static MoveResult Ok(GameState newState, params GameEvent[] events) =>
        new(newState, events, NoIssues);

    // Accepted, possibly with warnings attached.
    public static MoveResult Ok(GameState newState, IReadOnlyList<GameEvent> events, IReadOnlyList<Issue> warnings) =>
        new(newState, events, warnings);

    // A rejected move returns the ORIGINAL state, unchanged.
    public static MoveResult Rejected(GameState unchanged, string message) =>
        new(unchanged, Array.Empty<GameEvent>(), new[] { new Issue(Severity.Error, message) });
}
