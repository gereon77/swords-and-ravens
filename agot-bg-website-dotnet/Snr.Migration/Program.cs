using Microsoft.Extensions.Configuration;
using Snr.Migration;

// Connection strings are read from --legacy/--target if given, otherwise from user secrets
// (keys "Legacy"/"Target") so a production connection string never has to be typed on the
// command line (and so it never ends up in shell history or a process list):
//   dotnet user-secrets set "Legacy" "Host=...;Database=snr_django;..." --project Snr.Migration
//   dotnet user-secrets set "Target" "Host=...;Database=snr_dotnet;..." --project Snr.Migration
//
// dotnet run --project Snr.Migration -- import [--legacy "..."] [--target "..."]
// dotnet run --project Snr.Migration -- verify [--legacy "..."] [--target "..."]
// See MIGRATION_PLAN.md §10 for the design this implements.

var config = new ConfigurationBuilder()
    .AddUserSecrets(System.Reflection.Assembly.GetExecutingAssembly())
    .Build();

string? command = args.Length > 0 ? args[0] : null;
string? legacy = GetOption(args, "--legacy") ?? config["Legacy"];
string? target = GetOption(args, "--target") ?? config["Target"];
string? messagesDaysBackOption = GetOption(args, "--messages-days-back");
var messagesDaysBack = -1;
if (messagesDaysBackOption != null && !int.TryParse(messagesDaysBackOption, out messagesDaysBack))
{
    Console.WriteLine(
        $"Invalid --messages-days-back value '{messagesDaysBackOption}', expected an integer."
    );
    return 1;
}

if (command is not ("import" or "verify") || legacy == null || target == null)
{
    Console.WriteLine(
        """
        Usage:
          dotnet run -- import [--legacy "<connection string>"] [--target "<connection string>"] [--messages-days-back <n>]
          dotnet run -- verify [--legacy "<connection string>"] [--target "<connection string>"]

        --legacy/--target fall back to user secrets ("Legacy"/"Target") when omitted - see this
        file's top comment for the exact `dotnet user-secrets set` commands. Preferred over typing
        a production connection string directly on the command line.

        Imports Users, Groups/Roles, Rooms, Games, PlayerInGame, historical PreviousPlayerInGame,
        Messages and PbemResponseTime from a legacy Django database into a fresh
        agot-bg-website-dotnet Postgres database. Safe to re-run repeatedly (idempotent) — see
        MIGRATION_PLAN.md §10.

        Games cancelled while still in the lobby (view_of_game.turn == -1) are never imported (and
        are deleted from the target if an older run already imported one) — see §10, matching the
        live save-game endpoint's own cleanup-on-cancel behavior.

        The historical PreviousPlayerInGame backfill (§10.1) runs automatically as part of `import`,
        computed directly from each Finished/Cancelled game's ViewOfGame JSON (oldPlayerIds/
        timeoutPlayerIds) while it's already in memory for the games import - no re-query or
        SerializedGame parsing needed. It never touches games that already have PreviousPlayerInGame
        rows (e.g. from a genuine live game-server save).

        --messages-days-back controls how much chat history is imported: -1 (default) imports all
        messages, 0 imports none, and any positive N only imports messages younger than N days.
        """
    );
    return command is null ? 1 : 0;
}

var importer = new Importer(legacy, target, messagesDaysBack);
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
