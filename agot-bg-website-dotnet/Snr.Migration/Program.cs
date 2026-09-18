using CommandLine;
using Microsoft.Extensions.Configuration;
using Snr.Migration;

// Connection strings are read from --legacy/--target if given, otherwise from user secrets
// (keys "Legacy"/"Target") so a production connection string never has to be typed on the
// command line (and so it never ends up in shell history or a process list):
//   dotnet user-secrets set "Legacy" "Host=...;Database=snr_django;..." --project Snr.Migration
//   dotnet user-secrets set "Target" "Host=...;Database=snr_dotnet;..." --project Snr.Migration
//
// dotnet run --project Snr.Migration -- import [--legacy "..."] [--target "..."]
//     ^ DEPRECATED/DISABLED - see ImportOptions's HelpText and MIGRATION_PLAN.md §18. The legacy
//       Django server has been decommissioned and destroyed post-cutover, so there is no longer
//       any legacy database left to import from; this verb now always refuses to run.
// dotnet run --project Snr.Migration -- verify [--legacy "..."] [--target "..."]
// dotnet run --project Snr.Migration -- backfill-initial-players [--target "..."]
// dotnet run --project Snr.Migration -- <verb> --help   (per-verb usage/options)
//
// Parsed with CommandLineParser (verb-per-task) rather than the previous hand-rolled args[0]/
// GetOption switch, so that adding a future one-time migration/backfill tool (there will be more,
// see MIGRATION_PLAN.md §14/§15) only means adding one more [Verb] options record plus one more
// MapResult arm below - not touching the shared parsing/validation code at all.
// See MIGRATION_PLAN.md §10 for the design import/verify implement.

var config = new ConfigurationBuilder()
    .AddUserSecrets(System.Reflection.Assembly.GetExecutingAssembly())
    .Build();

return await Parser
    .Default.ParseArguments<ImportOptions, VerifyOptions, BackfillInitialPlayersOptions>(args)
    .MapResult(
        (ImportOptions o) => RunImportAsync(o),
        (VerifyOptions o) => RunVerifyAsync(o, config),
        (BackfillInitialPlayersOptions o) => RunBackfillInitialPlayersAsync(o, config),
        _ => Task.FromResult(1)
    );

/// <summary>
/// `import` is permanently disabled: it exists only so its code/history/HelpText remain as
/// documentation of the one-time Django-&gt;.NET migration (MIGRATION_PLAN.md §10/§18), which has
/// already run to completion for the real production cutover. The legacy Django/Dokku site was
/// then stopped and the droplet it ran on has since been destroyed, so there is no legacy database
/// left anywhere to import from - deliberately refuses to even look at --legacy/--target or touch
/// any connection, rather than merely failing once it tries (and can't) reach a legacy database.
/// </summary>
static Task<int> RunImportAsync(ImportOptions _)
{
    Console.WriteLine(
        "`import` is deprecated and permanently disabled: the legacy Django server has been "
            + "decommissioned and destroyed, so there is no legacy database left to import from. "
            + "See MIGRATION_PLAN.md §18 - the one-time production import already ran; this verb "
            + "is kept only for historical reference."
    );
    return Task.FromResult(1);
}

static async Task<int> RunVerifyAsync(VerifyOptions options, IConfigurationRoot config)
{
    var legacy = options.Legacy ?? config["Legacy"];
    var target = options.Target ?? config["Target"];
    if (legacy is null || target is null)
    {
        Console.WriteLine(
            "Missing --legacy/--target (and no \"Legacy\"/\"Target\" user secret configured)."
        );
        return 1;
    }

    var importer = new Importer(legacy, target);

    // Creates the target database/schema from scratch if it doesn't exist yet (idempotent
    // otherwise), so `verify` can run against a brand-new environment without first starting the
    // `website` app just to trigger its own startup-time Database.MigrateAsync() (see Program.cs)
    // - see MIGRATION_PLAN.md §17.4/§17.5.
    await importer.MigrateTargetAsync();
    await importer.VerifyAsync();
    return 0;
}

static async Task<int> RunBackfillInitialPlayersAsync(
    BackfillInitialPlayersOptions options,
    IConfigurationRoot config
)
{
    var target = options.Target ?? config["Target"];
    if (target is null)
    {
        Console.WriteLine(
            "Missing --target (and no \"Target\" user secret configured) for backfill-initial-players."
        );
        return 1;
    }

    await InitialPlayersBackfill.RunAsync(target);
    return 0;
}

/// <summary>Shared --legacy/--target options common to every verb that talks to both databases.</summary>
abstract class LegacyTargetOptions
{
    [Option(
        "legacy",
        HelpText = "Legacy Django database connection string. Falls back to the \"Legacy\" user secret."
    )]
    public string? Legacy { get; set; }

    [Option(
        "target",
        HelpText = "Target agot-bg-website-dotnet database connection string. Falls back to the \"Target\" user secret."
    )]
    public string? Target { get; set; }
}

[Verb(
    "import",
    HelpText = "[DEPRECATED - PERMANENTLY DISABLED] Used to import Users, Groups/Roles, Rooms, "
        + "Games, PlayerInGame, historical PreviousPlayerInGame, Messages and PbemResponseTime "
        + "from a legacy Django database into a fresh agot-bg-website-dotnet Postgres database - "
        + "see MIGRATION_PLAN.md §10/§18. The one-time production cutover import already ran and "
        + "the legacy Django server has since been decommissioned/destroyed, so this verb now "
        + "always refuses to run (no legacy database exists to import from anymore). Kept only so "
        + "its options/behavior remain documented for historical reference."
)]
class ImportOptions : LegacyTargetOptions
{
    [Option(
        "messages-days-back",
        Default = -1,
        HelpText = "How much chat history to import: -1 (default) imports all messages, 0 imports "
            + "none, and any positive N only imports messages younger than N days. Unused now that "
            + "this verb is permanently disabled - kept for historical documentation only."
    )]
    public int MessagesDaysBack { get; set; }
}

[Verb(
    "verify",
    HelpText = "Compares row counts between the legacy and target databases without importing anything."
)]
class VerifyOptions : LegacyTargetOptions;

[Verb(
    "backfill-initial-players",
    HelpText = "One-time tool (only needs --target, not --legacy) that fills in "
        + "`ViewOfGame.initialPlayerIds` for already-migrated Finished/Cancelled games that "
        + "predate the game server persisting that field itself - see "
        + "Snr.Migration/InitialPlayersBackfill.cs's doc comment and MIGRATION_PLAN.md §14. Unlike "
        + "the other backfills, this one does parse each candidate game's (potentially multi-MB) "
        + "SerializedGame, so it's noticeably slower per game and is never run automatically as "
        + "part of import/verify. Safe to re-run - already-backfilled games are skipped."
)]
class BackfillInitialPlayersOptions
{
    [Option(
        "target",
        HelpText = "Target agot-bg-website-dotnet database connection string. Falls back to the \"Target\" user secret."
    )]
    public string? Target { get; set; }
}
