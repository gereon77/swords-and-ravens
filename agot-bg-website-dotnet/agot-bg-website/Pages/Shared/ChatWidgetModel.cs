namespace agot_bg_website.Pages.Shared;

/// <summary>
/// View model for the <c>_ChatWidget</c> partial (public "chat" + "issues" rooms plus the
/// online-users list shown on Games/MyGames), mirroring Django's dual_chat.html/online_users.html
/// components. See MIGRATION_PLAN.md §7.
/// </summary>
public sealed record ChatWidgetModel(Guid PublicRoomId, Guid IssuesRoomId, bool IsAuthenticated);
