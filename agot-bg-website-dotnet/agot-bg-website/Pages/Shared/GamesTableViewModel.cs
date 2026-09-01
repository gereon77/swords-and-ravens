using agot_bg_website.Services.GameListing;

namespace agot_bg_website.Pages.Shared;

/// <summary>View model for the shared _GamesTable partial, reused across Games/MyGames and every
/// admin-only inactive/replacement-needed sub-list — the .NET equivalent of Django's
/// `games_table` template tag.</summary>
public sealed class GamesTableViewModel
{
    public required IReadOnlyList<GameListItem> Games { get; init; }

    /// <summary>Show the Open/Ongoing badge per row - needed whenever a list can mix both states (e.g. "My games").</summary>
    public bool ShowStateBadge { get; init; } = true;

    /// <summary>Show a "Join as &lt;waited player&gt;" action for admins/high members (replacement-needed list only).</summary>
    public bool ShowJoinAsWaited { get; init; }

    public bool CanPlayAsAnotherPlayer { get; init; }

    public bool IsAuthenticated { get; init; }

    public bool OnProbation { get; init; }

    public string EmptyMessage { get; init; } = "There are no games to show.";
}
