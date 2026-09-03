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

`Program.cs` conditionally registers each external OAuth provider (Google/Discord/Facebook)
**only if both its ClientId and ClientSecret are non-empty** in configuration. With the
placeholder-empty values checked into `appsettings.json`:
- the app starts up fine with zero OAuth app registrations configured;
- `/Identity/Account/Register` and `/Identity/Account/Login` (plain Identity forms) are the only
  sign-in options shown/available;
- `options.SignIn.RequireConfirmedAccount` is always `true`, in every environment (including local
  Docker debugging) — see Program.cs's comment on why: it exercises the real confirm-email flow
  everywhere rather than silently skipping it in one environment, and the local SMTP catcher
  (smtp4dev, see "Email" below) makes that painless to test against.

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

## Banned-user login block & post-login redirect (`Infrastructure/Auth/AppSignInManager.cs`)

- Mirrors Django's `User.is_active = False`, which made `authenticate()` refuse the credentials
  outright rather than only logging the member out again on their next game-join attempt (the
  latter is what `Api/PlayApi.cs`'s existing force-sign-out-on-join already did, and still does, as
  a second line of defense for a member who's banned while their session/cookie is still active).
- `AppSignInManager` (registered in `Program.cs`, replacing the `SignInManager<ApplicationUser>`
  that `AddIdentity()` registers by default) overrides `CanSignInAsync` to additionally refuse
  members in the `Banned` role. `CanSignInAsync` is the one choke point every sign-in path already
  funnels through via `PreSignInCheck` (password, external OAuth, 2FA), so this blocks banned
  members at all of them at once instead of duplicating the check per Account page.
  `Login.cshtml.cs`/`ExternalLogin.cshtml.cs` distinguish "banned" from other `IsNotAllowed`
  reasons (currently only "email not confirmed") by re-checking the `Banned` role, and redirect to
  the new `/Identity/Account/Banned` page instead of showing the generic error.
- Post-login redirect: `Infrastructure/Auth/ReturnUrlHelper.NormalizeAfterLogin` sends a visitor to
  `/Games` instead of back to the marketing home page (`/` or `/Index`) after logging in — clicking
  "Login" almost always means "I want to get to the Games list", and they've already seen the home
  page or they wouldn't be here. Any other return URL (a deep link the `[Authorize]` challenge
  captured, or a specific game/user page) is left untouched. `Pages/Shared/_LoginPartial.cshtml`'s
  manual Login/Register links now also pass the current page as `returnUrl` so this normalization
  actually has something other than "no returnUrl" to work with when a signed-out visitor clicks
  Login from an arbitrary public page.
- Verified via `dotnet build`/`dotnet test` (new `ReturnUrlHelperTests`, 10 cases) only — no live
  browser pass yet exercising an actual banned user hitting `/Identity/Account/Login` or an OAuth
  callback.

## Disposable email address blocking (registration & email change)

- Uses the `Soenneker.Validators.Email.Disposable.Online` NuGet package (free, actively maintained,
  no API key) rather than any paid verification service - it downloads the community-maintained
  [`disposable/disposable-email-domains`](https://github.com/disposable/disposable-email-domains)
  domain list once (lazily, cached for the app's lifetime) and checks the email's domain against it
  locally; the email address itself is never sent anywhere. Registered as a singleton in
  `Program.cs` (`AddEmailDisposableOnlineValidatorAsSingleton`); the list source URI can be
  overridden via `Validators:Email:Disposable:Uri` in config if a self-hosted/updated list is ever
  wanted instead of the GitHub default.
- `Services/DisposableEmailChecker.cs` wraps it and **fails open**: if the list download throws
  (offline dev box, GitHub outage, ...), the address is allowed through rather than blocking
  registration/email-change just because the block-list couldn't be fetched. Only a confirmed match
  against a successfully downloaded list refuses the address.
- Wired into the three places a member picks/changes their own email address: `Register.cshtml.cs`
  (password registration), `ExternalLogin.cshtml.cs`'s `OnPostConfirmationAsync` (new account via
  OAuth — only when actually creating a new account, not when linking an OAuth login to an existing
  account by email), and `Manage/Email.cshtml.cs`'s `OnPostChangeEmailAsync`.
- Verified via `dotnet build`/`dotnet test` (new `DisposableEmailCheckerTests`, 4 cases covering the
  three-state validator result and the fail-open behavior) — no live pass yet against the real
  online domain list (would require network access from the test run, deliberately avoided).

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
- Tongueless rate-limiting/regex enforcement and the private-message email path
  (`ChatWebSocketApi.NotifyChatPartnerAsync`) are now covered by unit tests
  (`agot-bg-website.Tests/Api/ChatWebSocketApiTests.cs`) against an in-memory DbContext/cache and a
  fake `IEmailSender` — PBEM-only sending, opted-out/no-other-player/missing-game no-ops, and the
  7-minute per-room/recipient dedupe window are all asserted directly, closing the "code-reviewed
  but not exercised" gap noted below from the initial WebSocket-chat pass. Still not separately
  exercised live end-to-end (no `Tongueless`-role test user / pbem game fixture was set up against a
  real socket/SMTP relay), but the underlying logic itself is now test-verified rather than just
  reviewed.

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

Only needed if you want to test Google/Discord/Facebook sign-in locally — plain username/password
accounts work with zero configuration (see above). Use user-secrets rather than committing real
values to `appsettings.json`:

```powershell
cd agot-bg-website
dotnet user-secrets set "Authentication:Google:ClientId" "..."
dotnet user-secrets set "Authentication:Google:ClientSecret" "..."
dotnet user-secrets set "Authentication:Discord:ClientId" "..."
dotnet user-secrets set "Authentication:Discord:ClientSecret" "..."
dotnet user-secrets set "Authentication:Facebook:ClientId" "..."
dotnet user-secrets set "Authentication:Facebook:ClientSecret" "..."
dotnet user-secrets set "GameServer:MasterApiPassword" "..."
```

(Facebook in particular needs a Meta developer app with app review — privacy policy URL and
business verification — before it works for real, non-test users; see MIGRATION_PLAN.md §12.)

> **Facebook login cannot be tested against plain `http://localhost:8000`.** Unlike Google/Discord,
> Meta's Facebook Login product rejects `http` redirect URIs even for local/test apps (only `https`
> is accepted, aside from a few disallowed loopback exceptions). Standing up a trusted HTTPS dev
> certificate purely for this would also complicate the game server's calls into the website (it
> would need to trust the self-signed cert too). Until the site is deployed somewhere with a real
> HTTPS endpoint (e.g. staging), skip local Facebook login testing — Google/Discord sign-in and
> local username/password auth already exercise the same account-linking code paths.

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
- Chat's tongueless rate-limiting and private-message email notification paths are now covered by
  unit tests (see the Chat section above) rather than only code-reviewed; still no live end-to-end
  pass with a real `Tongueless`-role test user / pbem game fixture against a real socket/SMTP relay.
- Banned-user login block and post-login redirect (see the section above) haven't had a live
  browser pass yet (real banned test user hitting Login/OAuth callback, real click-through of the
  home-page-to-Games redirect) - only `dotnet build`/`dotnet test` were used to verify.
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

## 9. Fixed: "Connection refused" when launching via VS's "Container (Dockerfile)" profile

After the fix above, running via the plain `http` (Project) launch profile worked, but Visual
Studio's other launch profile — **"Container (Dockerfile)"**, which builds and runs the app inside
its own Docker container instead of natively — then failed differently:

```
Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:5432
System.Net.Sockets.SocketException (111): Connection refused
```

Root cause: inside that container, `127.0.0.1` refers to the *container itself*, not the Windows
host — so it can never reach the `db`/`redis` containers, which are published on the host's
loopback interface, not inside the app's own container network namespace.

Fix: `Properties/launchSettings.json`'s `"Container (Dockerfile)"` profile now sets
`ConnectionStrings__DefaultConnection`, `ConnectionStrings__Redis`, and `Email__Host` environment
variables pointing at **`host.docker.internal`** (Docker Desktop's built-in DNS name for the host
machine, reachable from any container) instead of leaving them to fall back to
`appsettings.json`'s `127.0.0.1`. This only affects the container launch profile — the plain
`http` profile (native `dotnet run`/F5 without Docker) still uses `appsettings.json`'s
`127.0.0.1`, which is correct for that case since it isn't sandboxed inside a container network.

Verified by building the Docker image (`docker build -f agot-bg-website/Dockerfile .`) and running
it standalone with the same environment variables the profile now sets: role/room seeding queries
succeeded (no connection errors) and `/` returned 200.

**If you don't know which profile you're using:** in Visual Studio, check the debug target
dropdown next to the green "Run" button/F5 — it should say `http` (native, recommended for normal
day-to-day debugging) or `Container (Dockerfile)` (containerized, closer to the production
`website.Dockerfile` build, slower iteration). Both now work against the same Docker Postgres/Redis
containers described in §1/§7 without further configuration.

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

## 10. Fixed: auth-flow gaps found after real end-to-end usage (email confirm, duplicate/OAuth registration, nav, Admin area)

After actually using the running app, several real issues surfaced (not just cosmetic): email
confirmation wasn't gating login in Development, registering with an email already tied to an
external-login account wasn't blocked, the top nav was missing the Django site's main links, and
there was no way to manage users/games short of a raw SQL client. All fixed and verified live
against the real `swords-and-ravens-db-1`/`swords-and-ravens-redis-1`/`snr-smtp4dev-1` containers
via `dotnet run` + `curl` with a cookie jar (not just unit tests), end to end:

1. **Registered** a new local account (`POST /Identity/Account/Register`) → redirected to
   `RegisterConfirmation` (not signed in), confirming `RequireConfirmedAccount = true` now applies
   in Development too.
2. **Logged in before confirming** → got the new specific error message ("You need to confirm your
   email address before you can log in...") instead of the old generic "Invalid login attempt" —
   this was the actual root cause the maintainer originally reported as "logged in but nav still
   shows Login/Register": the sign-in was silently refused (`SignInResult.IsNotAllowed`), not a
   caching/rendering bug in `_LoginPartial.cshtml`.
3. **Fetched the confirmation email from smtp4dev's REST API** (`GET
   http://localhost:5099/api/Messages`, then `/plaintext` for the body) and followed the
   `ConfirmEmail` link → confirmed successfully.
4. **Logged in again** → `Set-Cookie: .AspNetCore.Identity.Application=...` issued, and `GET /`
   with that cookie now correctly rendered `Hello <email>!` / `Logout` instead of Login/Register —
   confirming the nav bug really was the unconfirmed-email case above, not a real rendering defect.
5. **Registered again with the same email** → blocked with "An account with this email already
   exists. Please log in instead." (the new `RegisterModel.BuildDuplicateAccountErrorMessage`
   logic, unit-tested in `RegisterModelTests.cs`).
6. **Granted the `Admin` role directly via SQL** (`INSERT INTO "AspNetUserRoles" ...`) to the test
   account, re-logged in (role claims are baked into the auth cookie at sign-in, so a DB-only role
   change needs a fresh sign-in or `RefreshSignInAsync` to take effect), then confirmed:
   - Anonymous `GET /Admin` → `302` redirect to login (blocked, as expected).
   - Signed-in non-admin would get the same block (the policy is role-based, not just
     "authenticated") — verified by testing before granting the role.
   - Signed-in admin: `GET /Admin`, `/Admin/Users`, `/Admin/Games` all returned `200` with the
     expected content (user list with a working Ban/Unban button, game list with a working
     View/Edit link).
   - Fixed one real bug found during this step: `options.Conventions.AuthorizeAreaFolder("Admin",
     "/", RoleNames.Admin)` doesn't take a role name — its third argument is an *authorization
     policy name*, so passing `"Admin"` directly threw `InvalidOperationException: The
     AuthorizationPolicy named: 'Admin' was not found` on first request. Fixed by registering a
     named `"AdminArea"` policy (`RequireRole(RoleNames.Admin)`) in `Program.cs` and referencing
     that policy name instead.
7. Cleaned up the test account (`DELETE FROM "AspNetUsers" WHERE "UserName"=...`) after
   verification.

`dotnet build` (0 errors) and `dotnet test` (33/33 passing, 4 new `RegisterModelTests`) both
re-verified after these changes.

## 11. Fixed: local Docker container returning "Empty reply from server" for every request

A long-running local `agot-bg-website` container (up ~3 hours, started before a `SnrMigration`
full-history import was run against the *same* local `snr_dotnet` Postgres database) stopped
answering any HTTP request at all — `curl http://localhost:8000/` returned `curl: (52) Empty
reply from server` on every attempt, while `docker logs` showed the container had started up
cleanly (`Application started. Press Ctrl+C to shut down.`) with no exceptions.

**Investigation:** built a fresh image from the exact same code
(`docker build -f agot-bg-website/Dockerfile .`) and ran it standalone against the same local
`db`/`redis` containers — it worked immediately (`200 OK`, full page HTML). This proves there was
**no code regression** in the "drop Development environment" changes; the old container had simply
gotten into a bad/hung state (most likely: its pooled Npgsql connections were left in a broken
state by the earlier bulk import against the same database, e.g. long lock waits or a dropped
connection the pool never recovered from). **Fix is operational, not a code change: recreate the
local container (`docker stop`/`rm` + re-`docker build`/`run`) any time it's been sitting idle
across a heavy local DB operation like a full `SnrMigration` import** — don't assume it'll notice
and reconnect on its own.

**Real (if unrelated) bug fixed along the way:** while comparing logs between the old and new
containers, both printed this on every single new Npgsql connection:

```
Cannot load library libgssapi_krb5.so.2
Error: libgssapi_krb5.so.2: cannot open shared object file: No such file or directory
```

This is non-fatal (Npgsql just skips GSSAPI auth and falls back to the configured auth method —
present in both the broken *and* the working container, so it was never the actual cause of the
empty replies), but it's noisy enough to obscure real errors in `docker logs` and adds a failed
`dlopen()` on every new physical connection. The Debian-slim `aspnet` base image doesn't ship the
library; fixed by installing the Debian package that provides it
(`libgssapi-krb5-2` — **not** `libkrb5-3`, which provides a different, unrelated `.so`) as root in
the Dockerfile's `base` stage before switching to `$APP_UID`. Verified: rebuilt, re-ran against the
same local Postgres, and the warning no longer appears; `/` still returns `200 OK`.

