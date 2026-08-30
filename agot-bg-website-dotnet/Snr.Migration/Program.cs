using Snr.Migration;

// dotnet run --project Snr.Migration -- import --legacy "Host=...;Database=snr_django;..." --target "Host=...;Database=snr_dotnet;..."
// dotnet run --project Snr.Migration -- verify --legacy "..." --target "..."
// See MIGRATION_PLAN.md §10 for the design this implements.

string? command = args.Length > 0 ? args[0] : null;
string? legacy = GetOption(args, "--legacy");
string? target = GetOption(args, "--target");

if (command is not ("import" or "verify") || legacy == null || target == null)
{
    Console.WriteLine("""
        Usage:
          dotnet run -- import --legacy "<connection string>" --target "<connection string>"
          dotnet run -- verify --legacy "<connection string>" --target "<connection string>"

        Imports Users, Groups/Roles, Rooms, Games, PlayerInGame, Messages and PbemResponseTime
        from a legacy Django database into a fresh agot-bg-website-dotnet Postgres database.
        Safe to re-run repeatedly (idempotent) — see MIGRATION_PLAN.md §10.

        Note: the historical PreviousPlayerInGame backfill is a separate step that runs from
        agot-bg-game-server (scripts/backfillPreviousPlayers.ts), not from here — see §10.1.
        """);
    return command is null ? 1 : 0;
}

var importer = new Importer(legacy, target);
if (command == "import")
{
    await importer.RunAsync();
    Console.WriteLine();
    await importer.VerifyAsync();
}
else
{
    await importer.VerifyAsync();
}
return 0;

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name)
            return args[i + 1];
    }
    return null;
}
