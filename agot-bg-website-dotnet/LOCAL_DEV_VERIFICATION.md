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
update` (against the `swords-and-ravens-db-1` container), `dotnet test` (29/29 passing), and a live
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

## Email notifications (`notify*` endpoints, `Services/SmtpEmailSender.cs`)

- `Api/NotificationsApi.cs` maps the 6 real Django `notify*` routes (confirmed from
  `api/urls.py`/`views.py` — the plan's "5 notify endpoints" summary undercounts by one):
  `notifyReadyToStart`, `notifyYourTurn`, `notifyBribeForSupport`, `notifyBattleResults`,
  `notifyNewVote`, `notifyGameEnded`, plus `addPbemResponseTime/{userId}/{responseTime}`. All are
  `POST /api/...`, gated behind the same `MasterApi` Basic Auth policy as the rest of `/api`
  (only the game server calls these, mirroring Django's `IsAdminUser`-via-trusted-caller model).
- Each `notify*` route takes `{ "users": ["<guid>", ...] }`, loads the `Game`, filters the given
  users to `EmailNotificationActive && Email != null`, and sends one email per qualifying user via
  `IEmailSender`, with subject/body ported line-for-line from the matching Django
  `agotboardgame_main/templates/agotboardgame_main/*_notification.html` template. The link embedded
  in the email is built from the live request (`{scheme}://{host}/play/{gameId}`), not a
  hardcoded base URL.
- `Services/SmtpEmailSender.cs` implements `IEmailSender` via a plain `System.Net.Mail.SmtpClient`,
  configured from an `Email:Host/Port/EnableSsl/Username/Password/FromAddress` config section.
  `Program.cs` only registers it (overriding Identity's built-in no-op `IEmailSender`) when
  `Email:Host` is non-empty — same "wire up only when configured" pattern already used for OAuth
  providers — so a fresh local checkout with an empty `Email` section silently no-ops (Identity's
  default sender) instead of throwing. `Email:EnableSsl` defaults to `true` (safe for a real
  external SMTP provider) but should be set to `false` for a local plain-SMTP test catcher — see
  below.
- Verified end-to-end via live `dotnet run` + HTTP against real `snr_dotnet` data: registered a
  user, inserted a `Games` row via `psql`, then `POST /api/notifyGameEnded/{gameId}` (Basic Auth as
  `game-server`) with that user's id returned `200 {"status":"ok"}` with no SMTP errors (no
  `IEmailSender` is even registered locally since `Email:Host` is empty); `addPbemResponseTime`
  returned `204` and inserted a row; an unknown `gameId` returned `404`. Cleaned up test rows after.

### Email (local testing) — no real SMTP account needed

The maintainer running this locally may not have (or want to use) the original production SMTP
credentials, and nothing SMTP-related should ever be committed to `appsettings.json`. The
recommended local setup uses a throwaway SMTP *catcher* instead of a real mail provider, so
registration/password-reset/notify* emails can be tested end-to-end without sending real mail or
needing any account/secret at all:

1. **`smtp4dev`** (`rnwood/smtp4dev`) is already wired into the repo root `docker-compose.yml` —
   `docker compose up -d smtp4dev` (or just `docker compose up -d` for everything) starts it
   alongside `db`/`redis`. It exposes a web UI at **http://localhost:5099** (view every email the
   app sends, no real inbox needed) and a plain SMTP listener on **2525** with no
   authentication/TLS required.
2. Point the app at it via **`dotnet user-secrets`** (from the `agot-bg-website` project folder) —
   this only writes to a per-user secrets file outside the repo, never to `appsettings.json`:
   ```powershell
   cd agot-bg-website
   dotnet user-secrets set "Email:Host" "localhost"
   dotnet user-secrets set "Email:Port" "2525"
   dotnet user-secrets set "Email:EnableSsl" "false"
   dotnet user-secrets set "Email:Username" ""
   dotnet user-secrets set "Email:Password" ""
   dotnet user-secrets set "Email:FromAddress" "no-reply@swordsandravens.local"
   ```
3. `dotnet run`, then register a new account at `/Identity/Account/Register` — the confirmation
   email shows up immediately in the smtp4dev web UI (http://localhost:5099), no real mailbox
   involved.
4. If you'd rather test against a *real* SMTP provider (e.g. your own mailbox with an app
   password, or a service like Mailtrap/SendGrid) instead of the local catcher, set the same
   `Email:*` keys via `dotnet user-secrets` with your provider's host/port/credentials and leave
   `Email:EnableSsl` at its default `true` — the app doesn't need any code changes either way.

Verified: with the above `smtp4dev` user-secrets, a live `dotnet run` + `POST
/Identity/Account/Register` produced a real SMTP send (plain, no TLS/auth) that smtp4dev's REST
API (`GET http://localhost:5099/api/Messages`) confirmed receiving, with the exact `From`/`To`/
`Subject` expected (`"Confirm your email"`). Test user cleaned up afterwards.

## Chat (WebSockets + Redis, `Infrastructure/Chat/*`, `Api/ChatWebSocketApi.cs`)

- Replaces Django Channels' `ChatConsumer` (`chat/consumers.py`) with raw ASP.NET Core WebSockets
  (`app.UseWebSockets()` + `context.WebSockets.AcceptWebSocketAsync()`) and Redis pub/sub for
  cross-instance fan-out — `ChatClient.ts`/`games_chat.html` needed **zero** changes, the wire
  JSON protocol (message types, snake_case field names) is preserved exactly.
- `RoomSeeder.SeedAsync` (called from `Program.cs` at startup, alongside `RoleSeeder`) idempotently
  creates/caches the two well-known `public`/`issues` rooms, mirroring Django's
  `get_public_room_id`/`get_issues_room_id`.
- `GET /ws/chat/room/{roomId}` (`Api/ChatWebSocketApi.cs`): checks `context.User.Identity
  .IsAuthenticated` (401 if not), 404s on an unknown room, 403s on a private room the caller has no
  `UserInRoom` row for yet (auto-creates one on first connect to public/issues or any room the
  caller is otherwise allowed into), then accepts the socket. Handles `chat_message` (persists +
  broadcasts via Redis, tongueless rate-limit/regex enforcement, private-room email notification),
  `chat_view_message` (updates `UserInRoom.LastViewedMessageId`), and `chat_retrieve`
  (`chat_messages_retrieved`/`more_chat_messages_retrieved`, same pagination semantics as Django's
  `get_and_transform_messages`).
- `ChatConnectionManager` (singleton) tracks this process's live sockets per room;
  `ChatBroadcaster` (singleton + `IHostedService`) subscribes to a Redis pub/sub pattern
  (`chat:room:*`) and relays incoming messages to the matching local sockets — this is the
  cross-instance equivalent of Django Channels' `channel_layer.group_send`.
  `ChatPresenceService` stores the public room's "who's online" list as one JSON blob per room in
  Redis (`chat:room:{roomId}:connected_users`), with 1-hour staleness pruning; pruned users get a
  personalized `force_disconnect` via an internal `__prune_check__` pub/sub message type that's
  never forwarded to browsers verbatim.
- Private-message email notifications: only for non-public rooms, only when the game's
  `ViewOfGame.settings.pbem == true`, only to the other `UserInRoom` occupant (not the sender),
  only if `EmailNotificationActive`, de-duped per `(roomId, recipientId)` for 7 minutes via
  `IMemoryCache` — ported from `notify_chat_partner` in `chat/consumers.py`.
- Verified end-to-end via live `dotnet run` + a small Node.js `ws` test script against the real
  `snr_dotnet` Postgres + the repo's Redis container: registered a user, connected to the seeded
  `public` room, received an initial `connected_users` broadcast and an empty
  `chat_messages_retrieved`, sent a `chat_message` and received it back; a second concurrent
  connection as the same user received the first connection's broadcast message live, and closing
  it produced a `connected_users` update showing the remaining connection; connecting without an
  auth cookie got a `401`. Test rows/user cleaned up afterwards.
- Not yet covered by this pass: tongueless rate-limiting and the private-message email path were
  code-reviewed against `chat/consumers.py` line-for-line but not separately exercised live (no
  `Tongueless`-role test user / pbem game fixture was set up) — both reuse already-verified building
  blocks (`RoleNames`/`UserManager.IsInRoleAsync`, `IEmailSender`) so the risk is low, but worth a
  manual pass before relying on them in production.

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
- `docker-compose up -d` from the repo root (`D:\_snr`), so the `db` (Postgres) *and* `redis`
  containers are running — this app shares both with the Django app (see above). Redis is
  required at startup (`ConnectionStrings:Redis`, default `localhost:6379`), not optional — it
  backs the chat WebSocket endpoint's cross-instance fan-out and presence tracking (see Chat
  above). `docker-compose.yml` also has an optional `smtp4dev` service for local email testing
  with zero real SMTP credentials — see "Email (local testing)" above.

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
  here, and hasn't been written yet — but it's fully feasible, not an actual gap in the data: the
  serialized game has tracked vote-out/replacement and clock-timeout-replacement log entries for
  ~3 years now, so `serialized_game.logs` already has everything needed to reconstruct removal
  history for that whole period once the script is written.
- `Snr.Migration` imports Users/Groups/Rooms/Games/PlayerInGame/Messages/PbemResponseTime; it does
  not import `UserInRoom` (deliberate — these rows are recreated naturally as users reconnect to
  chat rooms post-migration, same as a user's first-ever connect to a room today).
- Chat's tongueless rate-limiting and private-message email notification paths were code-reviewed
  line-for-line against `chat/consumers.py` but not separately exercised live (no `Tongueless`-role
  test user / pbem game fixture was set up) — see the Chat section above.
- See MIGRATION_PLAN.md §13 for further follow-up work intentionally deferred past this migration
  (precomputed statistics tables, public game statistics, UI library/theme).

## 8. Fixed: intermittent Npgsql "Cannot assign requested address" on startup (Windows)

Symptom (reported after running the built app from Visual Studio on Windows):

```
System.InvalidOperationException: An exception has been raised that is likely due to a transient failure.
 ---> Npgsql.NpgsqlException: Failed to connect to [::1]:5432
 ---> System.Net.Sockets.SocketException: Cannot assign requested address
   at ... RoleManager.RoleExistsAsync ... RoleSeeder.SeedAsync ... Program.<Main>
```

Root cause: `appsettings.json`'s connection strings used `Host=localhost`, which on Windows
resolves to the IPv6 loopback address `::1` first. Docker Desktop's port-forwarding proxy for
`0.0.0.0:5432`-published container ports does not reliably accept IPv6-loopback connections on
Windows, so the very first EF Core query (role seeding in `Program.cs`, via `RoleSeeder`) fails
before the app finishes starting.

Fix: changed `appsettings.json`'s `ConnectionStrings:DefaultConnection`/`ConnectionStrings:Redis`
from `localhost` to `127.0.0.1`, and the local `Email:Host` user-secret from `localhost` to
`127.0.0.1` (same underlying issue, would have hit smtp4dev the same way). Re-verified with
`dotnet run`: app starts cleanly, role/room seeding queries succeed, `/` returns 200. `dotnet
build`/`dotnet test` (29/29) re-confirmed after the change. See README.md's "Windows/Docker
Desktop note" for the same guidance.

## 7. UI theme (Tailwind CSS + DaisyUI dark rebrand)

Verified live via `dotnet run` + `curl`/`Invoke-WebRequest` against `http://localhost:5280`:

- `wwwroot/css/app.css` (Tailwind v3.4 + DaisyUI v4.12, custom `swordsandravens` dark theme) builds
  cleanly with `npm install && npm run build` inside `agot-bg-website/ClientAssets/` under this
  environment's Node 16.19.1 — confirmed the compiled CSS actually carries the custom theme's
  colors (e.g. `--p: 42.0802% 0.141541 20.035294`, the oklch conversion of the `#8a1f2b` crimson
  primary) rather than DaisyUI's stock palette.
- `/`, `/Privacy`, `/Identity/Account/Login`, `/Identity/Account/Register` all return 200, link
  `~/css/app.css`, set `<html data-theme="swordsandravens">`, and contain no remaining references
  to `bootstrap` (the vendor folder `wwwroot/lib/bootstrap/` was deleted; `wwwroot/lib/jquery*`
  kept, since `jquery-validation-unobtrusive` is unrelated to the visual framework).
  - Register/Login pages render scaffolded Identity markup (`form-control`, `row`/`col-md-*`,
    `text-danger`, etc.) through the Bootstrap-compatibility shim in `ClientAssets/src/app.css`,
    without any structural changes to the 40 scaffolded `Areas/Identity/Pages/**` files.
  - All user-visible "agot_bg_website"/"agot-bg-website" branding was replaced with
    "Swords and Ravens" in `_Layout.cshtml` and `Privacy.cshtml` (the only two files where it
    appeared as visible text — all other occurrences are `@using`/`@namespace` code references,
    intentionally left unchanged).
- `dotnet build` (0 errors) and `dotnet test` (29/29 passing) re-verified after all UI changes.
