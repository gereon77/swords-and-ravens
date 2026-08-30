# agot-bg-website-dotnet — local dev / verification

This solution is a hand-authored ASP.NET Core (Minimal API, not MVC controllers) implementation
of `MIGRATION_PLAN.md`.

## Solution layout

- **`agot-bg-website.Data`** — a plain class library with *only* the EF Core model: `Domain/`
  (entities), `Data/ApplicationDbContext.cs`, and `Migrations/`. No ASP.NET Core, no OAuth, no API
  code. This is deliberate: the future legacy-data migrator (`Snr.Migration`, MIGRATION_PLAN.md
  §10) and any other future tooling can reference this project alone to read/write the schema,
  without dragging in the web app's Razor Pages, Minimal API endpoints, or OAuth handlers.
- **`agot-bg-website`** — the ASP.NET Core web app (Identity UI, Minimal API groups, OAuth). Only
  project that references `agot-bg-website.Data`.
- **`Snr.Migration`** — a standalone console app (MIGRATION_PLAN.md §10) that imports Users,
  Groups/Roles, Rooms, Games, PlayerInGame, Messages, and PbemResponseTime from a legacy Django
  database into this schema. References only `agot-bg-website.Data`, not the web app.
- **`agot-bg-website.Tests`** — xUnit tests for the web app and `Snr.Migration` (references both,
  which transitively brings in `agot-bg-website.Data`).

## Status: fully verified against the repo's real Docker Postgres instance

`dotnet build` (solution-wide), `dotnet ef migrations add InitialCreate` + `dotnet ef database
update` (against the `swords-and-ravens-db-1` container), `dotnet test` (9/9 passing), and a live
`dotnet run` with an actual HTTP register → login round trip have all been run and verified in this
repo. Bugs found and fixed along the way:

- `UsersApi.cs`: `UserManager.GetRolesAsync` returns `IList<string>`, which doesn't implicitly
  convert to the `IReadOnlyList<string>` positional-record parameter on `UserDto` — fixed with an
  explicit `.ToList()` cast at the call site.
- `ApplicationDbContext.cs`: EF Core has no built-in mapping for `System.Text.Json.JsonDocument`
  (it looks like an owned/complex type to the model builder and fails at model-build time with
  "No suitable constructor was found for type 'JsonDocument'"). Fixed by adding a shared
  `ValueConverter<JsonDocument?, string?>` (round-trips through `RootElement.GetRawText()` /
  `JsonDocument.Parse`) and applying `.HasConversion(...).HasColumnType("jsonb")` to
  `Game.SerializedGame`, `Game.ViewOfGame`, and `PlayerInGame.Data`.
- **`Pages/Shared/_LoginPartial.cshtml` and the whole `Areas/Identity/Pages/**` tree were hard-coded
  to the default `IdentityUser` type** (that's how the VS template scaffolds them before you swap in
  a custom user class), which crashed every page with `No service for type
  'UserManager<IdentityUser>' has been registered` the moment Identity's UI was rendered (i.e. on
  every page, since `_Layout.cshtml` always renders `_LoginPartial`). Fixed by installing
  `dotnet-aspnet-codegenerator` and running
  `dotnet aspnet-codegenerator identity -dc agot_bg_website.Data.ApplicationDbContext --force`,
  which regenerates `Areas/Identity/Pages/**` correctly parameterized against `ApplicationUser`,
  then hand-fixing `_LoginPartial.cshtml` (not touched by that generator) to inject
  `SignInManager<ApplicationUser>`/`UserManager<ApplicationUser>` instead. The
  `Microsoft.VisualStudio.Web.CodeGeneration.Design` package was added only for that one command
  and removed again afterward — it's a design-time-only tool dependency, not needed at runtime.
- The codegen tool also auto-inserted a second, conflicting `builder.Services.AddDefaultIdentity<
  ApplicationUser>(...)` call into `Program.cs` (it doesn't recognize the existing hand-written
  `AddIdentity<...>().AddDefaultUI()` block as "Identity already configured"), causing `System.
  InvalidOperationException: Scheme already exists: Identity.Application` at startup. Removed the
  duplicate.

## Local dev: individual (username/password) accounts only, no OAuth required

`Program.cs` conditionally registers each external OAuth provider (Google/Discord/Instagram)
**only if both its ClientId and ClientSecret are non-empty** in configuration. With the
placeholder-empty values checked into `appsettings.json`:
- the app starts up fine with zero OAuth app registrations configured;
- `/Identity/Account/Register` and `/Identity/Account/Login` (plain Identity forms) are the only
  sign-in options shown/available;
- `options.SignIn.RequireConfirmedAccount` is set to `!builder.Environment.IsDevelopment()`, so
  in Development a freshly-registered local account can log in immediately without needing a
  working email sender to confirm the address (outside Development it stays `true`).

To add real OAuth later (locally or in production), just populate the corresponding ClientId/
ClientSecret via `dotnet user-secrets`/environment variables/`appsettings.*.json` — no code changes
needed, the provider registers itself automatically once configured.

## Database: reusing the repo's existing Docker Postgres container

The root `docker-compose.yml` already runs a Postgres container (`swords-and-ravens-db-1`,
image `postgres`, exposed on `localhost:5432`, user `postgres` / password `example`, per
`agot-bg-website/.env`). Rather than a separate instance, this app uses **a second database inside
that same container** (`snr_dotnet`), created once with:

```powershell
docker exec swords-and-ravens-db-1 psql -U postgres -c "CREATE DATABASE snr_dotnet;"
```

`appsettings.json`'s `ConnectionStrings:DefaultConnection` already points at
`Host=localhost;Port=5432;Database=snr_dotnet;Username=postgres;Password=example` to match. Start
the container stack from the repo root first if it isn't already running: `docker-compose up -d`.

## Data migration tool (`Snr.Migration`)

Console app, `dotnet run --project Snr.Migration -- import --legacy "<connection string>" --target
"<connection string>"` (or `verify` instead of `import` to only print row-count/id-sample checks).
Idempotent — re-running never duplicates rows, never touches a `Claimed` (already-logged-in-for-
real) user row, and only ever refreshes settings fields on still-unclaimed imported rows. Verified
end-to-end in this repo against a throwaway fake-legacy-schema database (real Django table/column
names, confirmed against `agotboardgame_main/models.py` and `chat/models.py`): first run imports
everything, second run is a no-op (0 imported, N updated), and marking one user `Claimed = true`
before a third run confirms that user's row is skipped entirely (`"claimed (skipped)"` count).
The historical `PreviousPlayerInGame` backfill (§10.1) is **not** part of this tool — it lives in
`agot-bg-game-server` per the plan, since it needs the TS `GameLogManager`/serialized-game replay
logic, and hasn't been written yet (see Known gaps below).

## Serving the game client (`/play`, `build_and_place_game_client_into_dotnet.ps1`/`.sh`)

- `build_and_place_game_client_into_dotnet.ps1`/`.sh` (repo root, `D:\_snr`) mirror
  `build_and_place_game_client_into_django.sh`: they build the game client
  (`yarn run build-local-client`) and copy `agot-bg-game-server/dist/*` into
  `agot-bg-website/wwwroot/static_game`, plus `dist/index.html` into
  `agot-bg-website/GameClientTemplates/play.html`.
- `agot-bg-game-server/public/index.html`'s Django `{{ auth_data|json_script:"auth-data" }}` tag
  was replaced with a framework-neutral `<!--AUTH_DATA_JSON-->` HTML comment placeholder (the one
  required change in the game-server repo per MIGRATION_PLAN.md §8.1).
- `Api/PlayApi.cs` maps `GET /play/{gameId}/{userId?}` (Minimal API, not an MVC controller, to
  match the rest of this app's REST surface) — equivalent of Django's `views.play`: bans force a
  sign-out + redirect to `/games`, users "On probation" can't join new lobby games, and a `userId`
  route value only takes effect (impersonation) for users in the `Admin`/`High Member` roles who
  aren't themselves already a player in that game. Reads `GameClientTemplates/play.html` if present,
  else falls back to the checked-in `play_fake.html`, and substitutes the `<!--AUTH_DATA_JSON-->`
  placeholder with a `<script id="auth-data" type="application/json">` tag (System.Text.Json's
  default encoder already escapes `<`/`>`/`&`, equivalent to Django's `json_script`).
- Roles used by the checks above (`Member`, `Admin`, `High Member`, `Banned`, `On probation`,
  `Tongueless` — see `Infrastructure/Auth/RoleNames.cs`) are seeded idempotently on every startup
  (`Infrastructure/Auth/RoleSeeder.cs`), so a fresh `snr_dotnet` database always has them even
  before `Snr.Migration` has imported anything.
- Verified end-to-end via live `dotnet run` + HTTP: registered/logged-in user hitting
  `/play/{gameId}` for a game row inserted directly via `psql` got back 200 with the correct
  `auth-data` JSON (`userId`/`requestUserId`/`gameId`/`authToken` all correct); unauthenticated
  request redirects (302) to `/Identity/Account/Login`; unknown `gameId` returns 404.

## GDPR basics

- A cookie-consent banner (`Pages/Shared/_CookieConsentPartial.cshtml`) is rendered on every page
  via `_Layout.cshtml`, backed by ASP.NET Core's built-in `CookiePolicyOptions`/
  `ITrackingConsentFeature` (`app.UseCookiePolicy()` in `Program.cs`).
- Identity's own sign-in cookies (`ApplicationScheme`/`ExternalScheme`) are explicitly marked
  `IsEssential = true`, so sign-in/sign-out keeps working even before a visitor accepts the banner
  (this mirrors the standard ASP.NET Core GDPR sample — see
  https://learn.microsoft.com/aspnet/core/security/gdpr).
- `Pages/Privacy.cshtml` has real (placeholder-for-legal-review) copy describing what account data
  is stored, what cookies are set, and how to request data export/deletion — replace with reviewed
  legal copy before going live.
- Per-user GDPR self-service (Art. 15/17) is already covered by the scaffolded Identity UI:
  `/Identity/Account/Manage/DownloadPersonalData` (exports all `[PersonalData]`-tagged
  `ApplicationUser` properties as JSON) and `/Identity/Account/Manage/DeletePersonalData` (deletes
  the account). `ApplicationUser`'s domain-specific fields (`GameToken`, `ProfileText`,
  `LastWonTournament`, `LastUsernameUpdateTime`, `LastActivity`, `CreatedAt`) are tagged
  `[PersonalData]` so they're included in the export alongside the base Identity fields
  (`UserName`/`Email`/`PhoneNumber`). Deleting a `PlayerInGame`'s `UserId` FK is `Restrict`, so a
  user genuinely mid-game cannot self-delete until the game ends — this is intentional (matches
  Django's behavior) but worth documenting to users.

## 1. Prerequisites

- .NET 10 SDK (`dotnet --version` should print `10.0.x`)
- `docker-compose up -d` from the repo root (`D:\_snr`), so the `db` (Postgres) container is
  running — this app shares it with the Django app (see above).

## 2. Restore & build

```powershell
cd D:\_snr\agot-bg-website-dotnet
dotnet restore
dotnet build
```

## 3. (Optional) Configure real OAuth secrets

Only needed if you want to test Google/Discord/Instagram sign-in locally — plain username/password
accounts work with zero configuration (see above). Use user-secrets rather than committing real
values to `appsettings.json`:

```powershell
cd agot-bg-website
dotnet user-secrets set "Authentication:Google:ClientId" "..."
dotnet user-secrets set "Authentication:Google:ClientSecret" "..."
dotnet user-secrets set "Authentication:Discord:ClientId" "..."
dotnet user-secrets set "Authentication:Discord:ClientSecret" "..."
dotnet user-secrets set "Authentication:Instagram:ClientId" "..."
dotnet user-secrets set "Authentication:Instagram:ClientSecret" "..."
dotnet user-secrets set "GameServer:MasterApiPassword" "..."
```

(Instagram in particular needs a Meta developer app with the "Instagram API with Instagram Login"
product — see MIGRATION_PLAN.md §12 for the email-availability caveat.)

## 4. Create the database schema

The real, Postgres/Guid-keyed `InitialCreate` migration already exists in
`agot-bg-website.Data/Migrations/`, and has already been applied to the `snr_dotnet` database
described above. If you're pointing at a fresh/different database, apply it with:

```powershell
cd agot-bg-website
dotnet ef database update
```

(`dotnet ef migrations has-pending-model-changes` currently reports no pending changes against the
checked-in migration, so you should not need to run `migrations add` again unless you change the
model. If you do change the model, run
`dotnet ef migrations add <Name> --project ..\agot-bg-website.Data\agot-bg-website.Data.csproj --startup-project agot-bg-website.csproj`
from the `agot-bg-website` folder, since the DbContext now lives in the separate Data project.)

## 5. Run tests

```powershell
cd D:\_snr\agot-bg-website-dotnet
dotnet test
```

`agot-bg-website.Tests` covers:
- `WinRateCalculatorTests` — the win-rate formula from MIGRATION_PLAN.md §10.2 (pure unit tests,
  no DB needed).
- `AccountLinkingServiceTests` — the claim/link pipeline from §5.3, against EF Core's InMemory
  provider.
- `ApplicationDbContextTests` — the `PreviousPlayerInGame` model configuration from §4.4 (multiple
  rows per game, cascade delete), against EF Core's InMemory provider.
- `ImporterTests` — `Snr.Migration`'s legacy game-state string → `GameState` enum mapping
  (`internal`, exposed to this test project via `InternalsVisibleTo`).
- `RoleNamesTests` — the fixed role list and the `Admin`/`High Member`-only
  `CanPlayAsAnotherPlayer` set that `PlayApi` checks against.

## 6. Run the app

```powershell
cd agot-bg-website
dotnet run
```

Then follow the rest of `MIGRATION_PLAN.md` §9 for wiring up the game server against it locally.

## Known gaps vs. the full plan (left for a follow-up pass)

- The historical `PreviousPlayerInGame` backfill script (§10.1) lives in `agot-bg-game-server`, not
  here, and hasn't been written yet (needs the TS `GameLogManager`/serialized-game replay logic).
- The `notify*` email endpoints in `NotificationsApi.cs` are stubbed (logged, not sent).
- Chat (§7, WebSockets) is not implemented yet.
- `Snr.Migration` imports Users/Groups/Rooms/Games/PlayerInGame/Messages/PbemResponseTime; it does
  not import `UserInRoom` (deliberately — see §10, these are recreated naturally as users
  reconnect to chat rooms, which isn't implemented yet since chat itself isn't).
