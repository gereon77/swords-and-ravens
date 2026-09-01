# Swords and Ravens — Django → ASP.NET Core Migration Plan

Status: design sketch, no code yet. Target: replace `agot-bg-website/` (Django) with a new
`agot-bg-website-dotnet/` app while keeping `agot-bg-game-server/` (Node/TS) essentially unchanged.

This document assumes familiarity with the current setup described in the root `README.md` and
`agot-bg-website/README.md`. Read those first if anything below seems to skip context.

## 1. Goals

1. Replace the Django website with ASP.NET Core, reusing PostgreSQL + Redis (already provisioned
   via `docker-compose.yml`).
2. Keep the Node/TS game server (`agot-bg-game-server/`) working with **no functional changes**
   to game logic — only the small integration seam (REST calls, generated client template,
   basic auth) needs to keep matching, plus one deliberate additive change described in §4.4/§6.1
   to support goal 7.
3. Support **three OIDC providers**: Google, Discord (both already supported today) **+ Instagram
   (new)**.
4. Add **local username/password authentication** (does not exist in Django today — Django only
   ever used `social_core.backends.google.GoogleOAuth2` / `DiscordOAuth2`, plus the Django admin
   password login which regular users never see).
5. Design a **fresh** database/schema (not an in-place Django migration), plus a **repeatable
   import tool** that copies data from the existing Django/Postgres database into the new schema.
6. When a user first authenticates on the new site (via any method) with an email that matches an
   **imported-but-unclaimed** legacy account, automatically link the new login to that account
   instead of creating a duplicate — mirroring Django's `associate_by_email` pipeline step.
7. Introduce a **`PreviousPlayerInGame`** table (missing from Django) that records players who were
   removed from a game (replaced by a vassal, replaced by another player, or timed out) so those
   games can be counted against their win rate instead of silently disappearing. See §4.4, §6.1,
   and §10.4.

## 2. Recommended stack

| Concern | Choice | Why |
|---|---|---|
| Web framework | ASP.NET Core MVC + Razor Pages (not Blazor) | See prior discussion: the interactive part of the product is the separate React/MobX game client; the website itself is server-rendered pages + a REST API + auth, which is MVC/Razor Pages' sweet spot. |
| ORM / DB | EF Core + Npgsql, PostgreSQL | Keeps the existing `docker-compose.yml` Postgres container; avoids a DB engine migration on top of a framework migration. |
| Auth | ASP.NET Core Identity | Built-in local username/password, external login linking table (`AspNetUserLogins`), extensible for exactly the "claim an imported account" flow needed here. |
| External OIDC/OAuth | `Microsoft.AspNetCore.Authentication.Google` (built-in) + `AspNet.Security.OAuth.Discord` + a small custom `OAuthHandler<T>` for Instagram (see §6.4) | Google/Discord have mature handlers; Instagram's modern "Instagram API with Instagram Login" needs a hand-rolled handler (details below) since there's no first-party or well-maintained Instagram OIDC package. |
| Realtime chat transport | **Plain ASP.NET Core WebSockets** (`app.UseWebSockets()` + a custom handler), *not* SignalR | The existing `ChatClient.ts` speaks a raw WebSocket with a small hand-rolled JSON protocol against Django Channels. Reimplementing the same raw protocol over native ASP.NET Core WebSockets means **zero changes to `ChatClient.ts`**. SignalR would require the TS client to switch to `@microsoft/signalr` and adopt its handshake/invocation protocol — a real, non-trivial migration cost for no functional gain. Revisit SignalR later only if you want typed hubs and are willing to touch the TS client. |
| Chat/game-state fan-out across instances | `StackExchange.Redis` pub/sub, same `REDIS_URL` | Direct replacement for `channels_redis`; still just Redis. |
| Background jobs (mail, etc.) | `IHostedService` / minimal in-process queue, or Hangfire if volume grows | Django used synchronous `send_mass_mail` in request handlers; low volume today, no need for a heavy queue yet. |
| Metrics | `prometheus-net.AspNetCore` | Replacement for `django-prometheus`. |
| Errors | `Sentry.AspNetCore` | Direct equivalent of `sentry-sdk`, already used by the Node game server too (`@sentry/node`), so both backends report to the same project. |
| Static files (game client bundle) | ASP.NET Core static files middleware serving `wwwroot/static_game/` | Direct equivalent of Django's `STATICFILES_DIRS` entry for `static_game`. |

## 3. Solution layout (mirrors the 3 Django apps 1:1)

```
agot-bg-website-dotnet/
  Snr.sln
  src/
    Snr.Web/                      # ~ agotboardgame project + agotboardgame_main app
      Program.cs                  # composition root: Identity, auth handlers, WebSockets, static files
      appsettings.json / appsettings.Development.json
      Areas/Identity/             # Identity UI (login, register, external login callback, claim flow)
      Controllers/ or Pages/      # index, about, rules, games, my_games, settings, user profile, play
      GameClientTemplates/        # NOT under wwwroot — holds the generated play.html "template"
      wwwroot/
        static_game/              # built game-client assets land here (git-ignored)
      Chat/
        ChatWebSocketMiddleware.cs   # ~ chat/consumers.py
        ChatPresenceCache.cs         # ~ get/add/remove connected_user helpers (IDistributedCache/Redis)
    Snr.Api/                      # ~ api app — REST endpoints consumed by the game server
      Controllers/
        UsersController.cs        # GET /api/user/{id}
        GamesController.cs        # GET/PATCH /api/game/{id}
        RoomsController.cs        # POST /api/room
        NotificationsController.cs# /api/notifyReadyToStart/{id}, notifyYourTurn, etc.
        PublicController.cs       # /api/public/game/{id}
      Auth/
        ServiceBasicAuthenticationHandler.cs  # HTTP Basic auth for the game-server's service account
    Snr.Domain/                   # Entities + enums, framework-agnostic
      Entities/ (User, Game, PlayerInGame, PbemResponseTime, Room, Message, UserInRoom, ...)
    Snr.Infrastructure/           # EF Core DbContext, migrations, Identity store customization
      SnrDbContext.cs
      Migrations/
    Snr.Migration/                # console app: one-off/repeatable importer from the Django DB
      Program.cs
      LegacyDbReader.cs           # raw SQL/Npgsql reads against the OLD Django database (read-only)
      Importers/ (UserImporter, RoomImporter, GameImporter, PlayerInGameImporter, ...)
  tests/
    Snr.Web.Tests/
    Snr.Migration.Tests/
  build_and_place_game_client_into_dotnet.ps1
  website.Dockerfile                # multi-stage: node build stage -> dotnet publish stage
  docker-compose.override.yml       # optional: point at same db/redis containers, different db name
```

This maps directly onto the current Django apps: `agotboardgame_main` → `Snr.Web` pages/controllers,
`api` → `Snr.Api`, `chat` → `Snr.Web/Chat`.

## 4. Data model (fresh schema, EF Core code-first)

### 4.1 Guiding rule: preserve IDs the game server already persists

The Node game server stores raw string IDs inside `Game.serialized_game`/`view_of_game` JSON
(`EntireGame.id`, `EntireGame.users` keyed by user id, `publicChatRoomId` / `privateChatRoomsIds`
pointing at chat `Room.id`). **These IDs must be preserved exactly during import**, or every
persisted, currently-in-progress game becomes unloadable:

- `User.Id` (GUID) — must match Django's `agotboardgame_main.User.id` exactly.
- `Game.Id` (GUID) — must match Django's `Game.id` exactly.
- `Room.Id` (GUID, chat) — must match Django's `chat.Room.id` exactly (referenced from inside
  `serialized_game` as `publicChatRoomId`/private chat room ids).
- `Message.Id` / `PlayerInGame.Id` / `UserInRoom.Id` — not referenced from TS-side JSON; safe to
  regenerate, but the importer will preserve them anyway since it's free and simplifies diffing.

### 4.2 Entities

```
User (Identity user, extended)
  Id                 Guid   PK  (= legacy Django user id when imported)
  UserName           string (unique, 3-18 chars, same validator as Django)
  NormalizedUserName string
  Email              string? (nullable — some legacy OAuth users may have no verified email)
  NormalizedEmail    string?
  EmailConfirmed     bool
  PasswordHash       string?           // null until user sets a local password
  SecurityStamp / ConcurrencyStamp     // standard Identity plumbing
  GameToken          string            // ~ Django game_token, used by game server as auth bearer
  ProfileText        string?
  LastWonTournament  string?
  EmailNotificationActive        bool  default true
  MuteGames                      bool  default false
  UseHouseNamesForChat           bool  default false
  UseMapScrollbar                bool  default true
  GameStateColumnRight           bool  default false // renders in-game state column on right; named
                                                       // UseResponsiveLayoutOnMobile in the legacy
                                                       // Django column/API JSON (never renamed there
                                                       // - see MIGRATION_PLAN.md §6 for the API translation)
  LastUsernameUpdateTime         DateTimeOffset?
  LastActivity                   DateTimeOffset
  VanillaForumUserId             int   default 0   // kept only if the forum integration is still wanted
  ImportedFromLegacy             bool  default false // true for rows created by Snr.Migration
  Claimed                        bool  default true  // false only for ImportedFromLegacy rows with no login yet
  CreatedAt                      DateTimeOffset

UserLogin (= AspNetUserLogins, built into Identity)
  LoginProvider ("Google" | "Discord" | "Instagram")
  ProviderKey   (subject/user id from the provider)
  UserId -> User.Id

Group / Role (= Django auth Group: "Member", "Admin", "High Member", "Banned", "On probation", "Tongueless")
  -> ASP.NET Identity Roles, same names, same GROUP_COLORS-style badge mapping kept in Snr.Web config.

Game
  Id                Guid PK (= legacy Django game id when imported)
  Name              string
  OwnerUserId        Guid FK -> User
  SerializedGame     jsonb?   // full resumable state, same shape the TS server already produces
  ViewOfGame         jsonb?   // denormalized summary, same shape as today
  Version            string?
  State              string   // IN_LOBBY | ONGOING | FINISHED | CLOSED | CANCELLED
  CreatedAt / UpdatedAt / LastActiveAt

PlayerInGame
  Id      Guid PK
  GameId  Guid FK -> Game
  UserId  Guid FK -> User
  Data    jsonb   // same per-player payload Django stores today

PreviousPlayerInGame   // NEW — does not exist in Django, see §4.4
  Id              Guid PK
  GameId          Guid   FK -> Game
  UserId          Guid   FK -> User          // the user who was removed/replaced
  House           string                     // house id they held, e.g. "stark"
  SequenceNumber  int                         // 0-based order of removal within the game; see §4.4
  Reason          string                      // "VOTE" | "CLOCK_TIMEOUT" | "REPLACED_BY_PLAYER"
  WasWinner       bool?                       // true if House ultimately won; null while game still ongoing
  ReplacedAt      DateTimeOffset?             // timestamp of the log entry that caused the removal
  CreatedAt       DateTimeOffset              // row insert time (audit only)
  // Unique index on (GameId, SequenceNumber) — see §4.4 for why this, not (GameId, UserId, House),
  // is the natural key (a user can theoretically be removed from the same house more than once).

PbemResponseTime
  Id            Guid PK
  UserId        Guid FK -> User
  ResponseTime  int
  CreatedAt     DateTimeOffset

Room (chat)
  Id                Guid PK (= legacy Django chat room id when imported)
  Name              string
  Public            bool
  MaxRetrieveCount  int?
  CreatedAt         DateTimeOffset

Message (chat)
  Id        long/Guid PK
  RoomId    Guid FK -> Room
  UserId    Guid FK -> User
  Text      string (<=200 chars)
  CreatedAt DateTimeOffset

UserInRoom (chat)
  Id                  Guid PK
  UserId              Guid FK -> User
  RoomId              Guid FK -> Room
  LastViewedMessageId Guid? FK -> Message
```

All `jsonb` columns keep using Postgres `jsonb` via Npgsql — no shape change needed on the game
server side; `SerializedGame`/`ViewOfGame` remain opaque blobs the TS server owns.

### 4.3 New database, not an in-place upgrade

Provision a **new** Postgres database (e.g. `snr_dotnet`) alongside the existing Django database
in the same Postgres instance (or a second one), so:

- Django keeps running unmodified against its own DB during the transition.
- EF Core migrations start from an empty schema — no risk of Django's migration history/format
  interfering.
- The importer (§7) connects to *both* databases at once (legacy read-only, new read/write).

### 4.4 `PreviousPlayerInGame` — fixing a gap that was never in Django

**The problem today:** `PlayerInGame` is fully recalculated from `IngameGameState.players` on
every `saveGame` (`api/serializers.py::GameSerializer.update` does
`instance.players.all().delete()` then recreates rows from the `players` array in the PATCH body —
see `GameSerializer.update` and `IngameGameState.getPlayersInGame()` /
`EntireGame.getPlayersInGame()`). When a player is removed from a game — replaced by a vassal
(Mother of Dragons mechanic) or by another human player, or timed out — they simply vanish from
`IngameGameState.players`, so the next save deletes their `PlayerInGame` row entirely. The game
stops counting for or against them, silently. This was never added to Django because doing it
correctly requires replaying event history, not just diffing current state — this section makes
that tractable.

**The data already exists, it's just not surfaced.** The game server already tracks enough to
reconstruct full removal history, it's just never sent to the website:

- `IngameGameState.oldPlayerIds` / `timeoutPlayerIds` / `replacerIds` — deduplicated user-id lists
  (`IngameGameState.ts`), useful as a quick membership check but not ordered, not per-house, and
  missing which house/outcome was involved.
- `IngameGameState.gameLogManager.logs` (`GameLogManager.ts`, `@observable logs: GameLog[] = []`,
  **append-only, never trimmed**) — the authoritative, ordered history, containing everything
  needed to reconstruct exactly which user held which house and when they stopped:
  - `"user-house-assignments"` (`GameLog.ts:345`) — `{ assignments: [userId, houseId][] }`, logged
    once at game start.
  - `"player-replaced"` (`GameLog.ts:958`) — `{ oldUser, newUser?, house, reason? }`. `reason`
    (`ReplacementReason.VOTE | CLOCK_TIMEOUT`) is present when replaced by a vassal
    (`IngameGameState.replacePlayerByVassal`, `IngameGameState.ts:1117`); when a house is handed
    directly to another human via a vote (`VoteType.ts::ReplacePlayer.executeAccepted`, no vassal
    stint), `newUser` is set and `reason` is absent — treat that case as `"REPLACED_BY_PLAYER"`.
  - `"vassal-replaced"` (`GameLog.ts:967`) — `{ house, user }`, logged when a vassal house is later
    claimed by a new human player (`VoteType.ts::ReplaceVassalByPlayer`) — this starts a *new*
    stint for `user`, it does not by itself end anyone's stint.
  - `"winner-declared"` (`GameLog.ts:432`) — `{ winner: houseId }`, logged once when the game ends.
  - Every `GameLog` entry carries a `time: Date` (`GameLog.ts:7`), giving an exact timestamp for
    each removal.

Replaying these events in order for a given game reconstructs, per house, the ordered list of user
stints. Every stint except the last (still-current, or never-removed) one becomes one
`PreviousPlayerInGame` row: the user held `House` until `ReplacedAt`, for `Reason`, and `WasWinner`
records whether that house went on to win. A house can pass through more than 2 users (player →
vassal → different player → vassal again, etc.), which is why the natural key is
`(GameId, SequenceNumber)` — the 0-based index of the stint-ending event in that game's replay —
rather than `(GameId, UserId, House)`, which is not guaranteed unique if the same user is removed
from the same house twice in one game.

**Where this logic should live:** the log schema (`GameLogData`) is owned by
`agot-bg-game-server`, and it already owns the version-migration logic needed to safely read old
`serialized_game` blobs (`serializedGameMigrations.ts`, `GlobalServer.migrateSerializedGame`). Both
the ongoing capture (§6.1) and the historical backfill (§10.4) therefore reuse the *same* TS
replay logic instead of re-implementing GameLog parsing in C# — keeping the .NET/importer side
agnostic of the log schema's internal shape, consistent with how `SerializedGame`/`ViewOfGame` are
already treated as opaque blobs elsewhere in this plan.

## 5. Authentication design

### 5.1 Local username/password (new capability)

Standard ASP.NET Core Identity password login/registration pages. Password policy should be at
least as strict as Django's `AUTH_PASSWORD_VALIDATORS` (min length, not-too-common, not fully
numeric, not similar to username) — replicate with Identity's `PasswordOptions` +
`IPasswordValidator` (built-ins mostly cover this; add a small custom validator for the
"not-too-similar-to-username" check).

### 5.2 External providers

- **Google** — `AddGoogle(...)` using the same `SOCIAL_AUTH_GOOGLE_OAUTH2_KEY/SECRET` values
  (rename to `Authentication:Google:ClientId/ClientSecret`).
- **Discord** — `AddDiscord(...)` from the community `AspNet.Security.OAuth.Providers` package,
  same client id/secret, request `identify email` scope (same as
  `SOCIAL_AUTH_DISCORD_SCOPE = ["identify", "email"]` today).
- **Instagram (new)** — Instagram's old "Basic Display API" (which many old OSS OAuth handlers
  targeted) is deprecated. The current supported flow is **"Instagram API with Instagram Login"**
  (Meta), OAuth2-based but **frequently does not return an email address at all** — only
  `user_id` and `username`. Implication for this project:
  - Implement a small custom `OAuthHandler<OAuthOptions>` (same pattern as the Discord package)
    hitting Instagram's `/oauth/authorize` + `/oauth/access_token` + Graph `me` endpoint.
  - Because email may be missing, the post-login pipeline (§5.3) must handle "no email available"
    as a distinct case: create/link the account by provider id alone, and show a one-time
    "confirm your email" prompt (optional, not blocking) so the account *can* later be matched to
    an imported legacy row or used for notifications. Don't assume Instagram behaves like
    Google/Discord here — flag this to the user/stakeholders before committing to feature parity
    claims ("login with Instagram" ≠ "email-verified login with Instagram").

### 5.3 Account linking / "claiming" pipeline

Runs after any successful external login callback, and also on local registration. Conceptually a
straight port of Django's `social_core.pipeline.social_auth.associate_by_email` step:

```
OnExternalLoginCallback(provider, providerKey, emailFromProvider, displayName):
    existingLogin = FindByLogin(provider, providerKey)
    if existingLogin != null:
        SignIn(existingLogin)                       # returning user, done
        return

    if emailFromProvider != null:
        candidate = Users.SingleOrDefault(u =>
            u.NormalizedEmail == Normalize(emailFromProvider) &&
            u.ImportedFromLegacy && !u.Claimed)
        if candidate != null:
            AddLogin(candidate, provider, providerKey)
            candidate.Claimed = true
            SignIn(candidate)                        # imported account just got linked
            return

        candidateAlreadyClaimed = Users.SingleOrDefault(u =>
            u.NormalizedEmail == Normalize(emailFromProvider) && u.Claimed)
        if candidateAlreadyClaimed != null:
            # Same email, but already linked to a different login method.
            # Do NOT silently merge — show "an account with this email already exists,
            # sign in with your original method or contact support" (avoids account takeover
            # via a spoofed/re-registered email at a provider).
            ShowConflictPage()
            return

    NewUser = CreateUser(username: SuggestUsernameFrom(displayName), email: emailFromProvider)
    AddLogin(NewUser, provider, providerKey)
    SignIn(NewUser)

OnLocalRegister(username, email, password):
    candidate = Users.SingleOrDefault(u =>
        u.NormalizedEmail == Normalize(email) && u.ImportedFromLegacy && !u.Claimed)
    if candidate != null:
        SetPassword(candidate, password)             # "claim by setting a password"
        candidate.Claimed = true
        SignIn(candidate)
        return
    # else: normal Identity CreateAsync(...) flow, subject to the uniqueness constraints
```

Key points to keep from Django's behavior:
- Matching is **by normalized email only**, same as `associate_by_email`.
- An imported row is only auto-linked **once** (`Claimed` flips permanently) — subsequent
  logins from a second, different email never re-trigger linking against an already-claimed row.
- Already-claimed collisions are surfaced, not silently merged (Django's pipeline has this same
  property implicitly, since `associate_by_email` only fires when `social_user` didn't already
  find an association).

## 6. REST API for the game server (`Snr.Api`)

Goal: **the TypeScript `WebsiteClient`/`LiveWebsiteClient.ts` contract in `agot-bg-game-server/src/server/website-client/` should not need to change at all** — same routes, same JSON field names, same HTTP verbs, same Basic Auth. Only the base URL host/port changes (still just `MASTER_API_BASE_URL`).

| Django route (`api/urls.py`) | ASP.NET Core equivalent | Notes |
|---|---|---|
| `GET /api/user/{id}` | `UsersController.Get(Guid id)` | Same fields: `id, username, game_token, is_staff, mute_games, use_house_names_for_chat, use_map_scrollbar, use_responsive_layout_on_mobile, groups`. The DB column/CLR property backing the last one is `GameStateColumnRight` (renamed to match what it actually controls - see §4.2); `UserDto` pins the JSON key back to `use_responsive_layout_on_mobile` via `[JsonPropertyName]` so the game server needs no change. |
| `GET/PATCH /api/game/{id}` | `GamesController.Get/Patch` | `PATCH` body: `serialized_game, state, version, view_of_game, players[], update_last_active` — same partial-update semantics as `GameSerializer.update`. **Plus new optional `previous_players[]` field, see §6.1.** |
| `POST /api/room` | `RoomsController.Create` | Same body shape (`name, public, users, max_retrieve_count`). |
| `POST /api/notifyReadyToStart/{gameId}` etc. (5 notify endpoints) | `NotificationsController` actions | Same mail templates, same "only email users with notifications enabled" filter. |
| `POST /api/addPbemResponseTime/{userId}/{responseTime}` | `NotificationsController.AddPbemResponseTime` | |
| `DELETE /api/clearChatRoom/{roomId}` | `ChatAdminController.ClearRoom` | |
| `GET /api/game/{id}/isCancelled` | `GamesController.IsCancelled` | |
| `GET /api/public/game/{id}` | `PublicController.GetGameView` | Same sanitization: strip `replacerIds, oldPlayerIds, waitingForIds, publicChatRoomId, timeoutPlayerIds`; rename `turn` → `round`. Anonymous/unauthenticated (matches Django's actual `@permission_classes([])`/AllowAny — `api/PUBLIC_API.md`'s "Authentication Required: Yes" is stale docs, contradicted by the view's own docstring), so third-party sites can fetch live game state directly without forwarding cookies or hitting CORS/credentials issues. Documented at `/api/docs` via the built-in ASP.NET Core OpenAPI generator (`Microsoft.AspNetCore.OpenApi`, not Swashbuckle) — see §6.3. |

Implementation note: these endpoints are implemented as **Minimal API** endpoint groups
(`MapGroup("/api")...MapGet/MapPost/MapPatch`), not MVC controllers — no `[ApiController]` classes,
matching current ASP.NET Core guidance for small/medium JSON APIs. Identity's own login/register
UI still uses Razor Pages (that's how `Microsoft.AspNetCore.Identity.UI` ships), so the project has
no controller classes at all.

Auth for the service-to-service endpoints: replicate DRF's `BasicAuthentication` +
`IsAdminUser` with a small `AuthenticationHandler<BasicAuthenticationSchemeOptions>` that checks
credentials against `MASTER_API_USERNAME`/`MASTER_API_PASSWORD` config values (the same two env
vars the game server already sends — see `LiveWebsiteClient.ts`). No code changes needed on the
Node side.

**Bug found & fixed while first testing against the real `LiveWebsiteClient`:** the DTOs in
`Api/Dtos.cs` were written assuming a global `JsonNamingPolicy.SnakeCaseLower` was configured (its
header comment says so), but that configuration was never actually added to `Program.cs`. Every
response therefore serialized with ASP.NET Core Minimal API's default (camelCase) instead —
`UserDto.GameToken` came back as `"gameToken"`, not `"game_token"`, which is the exact field
`LiveWebsiteClient.ts`'s `getUser()` reads (`response.game_token`). That silently produced
`undefined` on the TS side, so `GlobalServer.ts`'s `userData.token != authToken` authentication
check always failed once real (non-default) `MASTER_API_USERNAME`/`MASTER_API_PASSWORD`
credentials were configured on both sides — this had gone unnoticed until then because nothing
had exercised the real HTTP round-trip before. Fixed with a single
`builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy =
JsonNamingPolicy.SnakeCaseLower)` in `Program.cs`; `PropertyNamingPolicy` applies to both
directions, so this also fixes `GamesController.Patch`'s body binding (`serialized_game`,
`view_of_game`, `update_last_active`, ...) and `RoomsController.Create`'s `max_retrieve_count`,
neither of which had been exercised end-to-end before either. One endpoint needed an explicit
override on top of this: `GET /api/game/{id}/isCancelled` returns a bare `{ "cancelled": bool }`
(not `{ "is_cancelled": bool }`), matching `LiveWebsiteClient.ts`'s `isGameCancelled()` which reads
`response.cancelled` — that property is named `cancelled` directly in the anonymous response
object rather than relying on the naming policy to derive it from `IsCancelled`.
`agot-bg-website.Tests/Api/ApiJsonNamingPolicyTests.cs` pins the snake_case wire format down for
`UserDto`/`GameDto`/`CreateRoomDto` so this can't silently regress back to camelCase again.

### 6.1 `previous_players` — the one deliberate TS change in this whole plan

Every other endpoint above needs **zero** game-server code changes. This one field is the single
exception, needed to populate `PreviousPlayerInGame` (§4.4) going forward for new/ongoing games.

**New TS method**, following the exact style/placement of the existing
`IngameGameState.getPlayersInGame()` (`IngameGameState.ts:790`) /
`EntireGame.getPlayersInGame()` (`EntireGame.ts:792`) pair:

```ts
// IngameGameState.ts
getPreviousPlayersInGame(): { userId: string; house: string; reason: string; wasWinner: boolean | null; replacedAt: Date; sequenceNumber: number }[] {
    // Replay this.entireGame's gameLogManager.logs in order, tracking current holder per house
    // starting from the "user-house-assignments" entry. Each "player-replaced" / "vassal-replaced"
    // entry that *ends* a stint emits one row here (see §4.4 for exact log-shape mapping).
    // wasWinner is derived by checking whether `house` appears as `winner` in a later
    // "winner-declared" entry (null if the game hasn't finished yet).
}
```

```ts
// EntireGame.ts — thin passthrough, mirroring getPlayersInGame()
getPreviousPlayersInGame() {
    return this.ingameGameState?.getPreviousPlayersInGame() ?? [];
}
```

**Wiring into the save path** (`GlobalServer.saveGame()`, `WebsiteClient.ts` interface,
`LiveWebsiteClient.ts`, `LocalWebsiteClient.ts`):

- `WebsiteClient.updateGame(...)` gains an optional `previousPlayers` parameter alongside the
  existing `players` parameter.
- `LiveWebsiteClient.ts` serializes it into the PATCH body as `previous_players`, one object per
  row: `{ user: userId, house, reason, was_winner, sequence_number, replaced_at }` — same
  snake_case convention as the rest of the payload.
- `LocalWebsiteClient.ts` (used for local/offline play with no website) just logs it, same as it
  already no-ops most other website-bound calls.
- `GamesController.Patch` on the .NET side treats `previous_players`, when present, as a **full
  replace** for that game — delete existing `PreviousPlayerInGame` rows for `GameId` and re-insert
  — mirroring the existing "delete all + recreate" idempotent pattern `GameSerializer.update`
  already uses for `players[]`. This makes repeated saves of the same ongoing game safe to retry.

**Post-implementation fix (local dev, live-tested):** the first real save-after-seating hit a
`DbUpdateConcurrencyException` ("expected to affect 1 row(s), but actually affected 0 row(s)") on
every single PATCH that added a player, not just under concurrent requests. Root cause: the new
`PlayerInGame`/`PreviousPlayerInGame` rows were assigned straight to the tracked `Game`'s
navigation collection (`game.Players = newList`) without ever calling
`db.PlayersInGame.AddRange(...)`. Because each row's `Id` is a client-set (non-default) `Guid`, EF
Core's automatic graph fixup assumed the row already existed and issued an `UPDATE` instead of an
`INSERT` — which affects 0 rows for a row that was never inserted. Fixed in `GamesApi.cs` by
explicitly calling `AddRange` for both replacement lists before assigning them to the navigation
properties; pinned down by `GamesApiPlayerReplacementTests`. Separately, `GamesApi.cs`'s PATCH
handler also acquires a per-game `Infrastructure.GameSaveLock` (an in-memory `SemaphoreSlim` keyed
by game id) as defense-in-depth, since the game server's fire-and-forget `saveGame()` can
genuinely fire overlapping saves for the same game — this wasn't the cause of the reported
exception, but is worth keeping since it's a real possible race on the delete side. Note this lock
only works for a single-process deployment; a horizontally-scaled deployment would need a
distributed lock instead (e.g. a Postgres advisory lock).

### 6.2 Network-isolating the private API from the public port (implemented)

The service-to-service endpoints in §6 (`Api/UsersApi.cs`, `GamesApi.cs`, `RoomsApi.cs`,
`NotificationsApi.cs` — everything gated by `MasterApiAuthenticationHandler`, i.e. Basic Auth
against `MASTER_API_USERNAME`/`MASTER_API_PASSWORD`) are, in the current Django deployment,
reachable on the same public port as the rest of the website and rely solely on that Basic Auth
credential for protection. Since a Docker Compose network already lets sibling containers reach
any port a container listens on via its service DNS name — whether or not that port is
`ports:`-published to the host — we can add network-level isolation as defense-in-depth without
Kubernetes, by giving these routes their own **internal-only** Kestrel endpoint that is simply
never published to the host.

**Two named Kestrel endpoints**, configured in `appsettings.json`'s `Kestrel:Endpoints` section, in
base config so the same values apply identically to plain `dotnet run` and to the Docker container
(this matters — see the pitfall below):
- `Public` — `http://0.0.0.0:8000` (everything user-facing: Razor Pages, `PublicApi`, `PlayApi`,
  chat WebSocket, static assets). Matches the Dockerfile's `EXPOSE 8000` and the "Container
  (Dockerfile)" launch profile's `httpPort`, and the old Django dev port for familiarity.
- `GameServerApi` — `http://0.0.0.0:8001` (only the four private route groups above). Only this
  port needs to be reachable by the game-server container; in production compose, only `Public`
  should be listed under `ports:`, leaving `GameServerApi` reachable solely from sibling containers
  on the same compose network (point the game server's `MASTER_API_BASE_URL` at
  `http://<website-service-name>:8001` there).

  **⚠️ Staging/production deployment note:** the local `launchSettings.json` Container profile
  publishes `8001` to the host too (see pitfall #3 below) purely so a host-run `yarn run
  run-server` game server can reach it in local dev. **Do not carry that over to the staging/
  production `docker-compose.yml`** — there, `GameServerApi` (`8001`) must be left out of
  `ports:` entirely, since both the website and game server run as sibling containers on the same
  compose network there and can already reach each other by service name without any host
  publishing. Publishing `8001` to the host in production would defeat the whole point of this
  feature (network-level isolation of the Basic-Auth-only API).

**Pitfall #1 found & fixed: don't put an environment-specific override on `Kestrel:Endpoints` for a
port VS's Docker profile also relies on.** The first version of this had `Public` bound to
`0.0.0.0:8080` in base config, overridden to `localhost:8000` only in
`appsettings.Development.json` for local `dotnet run` convenience. But `ASPNETCORE_ENVIRONMENT=
Development` is *also* the environment VS's "Container (Dockerfile)" debug profile runs with, so
that override silently applied inside the container too, on top of being loopback-only. Fixed by
putting the real, final port values (`8000`/`8001`) directly in the environment-agnostic base
`appsettings.json` instead of layering a dev-only override on top — one value, used identically
everywhere.

**Pitfall #2 found & fixed: VS's own Docker debug port mapping doesn't read `appsettings.json` at
all.** Even after fixing pitfall #1, F5-debugging via the "Container (Dockerfile)" profile still
produced `ERR_EMPTY_RESPONSE`. Root cause, found by inspecting the actual running debug container
with `docker inspect`: Visual Studio's Docker debug tooling decides which **container-side** port
to map `httpPort` (the host port, `8000` in `launchSettings.json`) to based on its own
`ASPNETCORE_HTTP_PORTS` environment variable — defaulting to `80` if that variable isn't set in the
launch profile — regardless of the Dockerfile's `EXPOSE` directives or anything in
`appsettings.json`'s `Kestrel:Endpoints`. Since our Kestrel config always overrides
`ASPNETCORE_HTTP_PORTS` for the app's *own* binding (see below), the app inside the container was
correctly listening on `8000`/`8001` the whole time — but VS was mapping host `8000` to container
`80`, where nothing listens, producing an empty response. The original (pre-port-split)
`launchSettings.json` had an explicit `"ASPNETCORE_HTTP_PORTS": "8080"` in the Container profile
for exactly this reason; it was mistakenly removed as "redundant" when the port split was first
implemented, since it has no effect on the app's actual Kestrel binding once `Kestrel:Endpoints` is
configured — but VS's own port-mapping decision still needs it. Fixed by restoring
`"ASPNETCORE_HTTP_PORTS": "8000"` in the Container profile's `environmentVariables` (updated to the
new port number). Verified by inspecting a real `docker run`/`docker inspect` reproduction of VS's
mapping behavior end-to-end, not just a bare `dotnet run`.

**Kestrel endpoint config replaces, not merges with, URL-based binding** — confirmed empirically:
the moment `Kestrel:Endpoints` (or `ConfigureKestrel`/`ListenAnyIP` in code) is present, Kestrel
logs `Overriding address(es) '...'. Binding to endpoints defined via IConfiguration and/or
UseKestrel() instead.` and stops honoring `ASPNETCORE_URLS`/launchSettings' `applicationUrl`/
`ASPNETCORE_HTTP_PORTS` entirely for the app's own binding — but see pitfall #2 above, VS's own
Docker port-mapping tooling still reads `ASPNETCORE_HTTP_PORTS` independently of that.

**Pitfall #3 found & fixed: local (non-Docker) game-server dev needs `GameServerApi` published
too.** `docker-compose`'s "sibling containers can reach unpublished ports" story (the whole
premise of this feature) only holds when the game server *also* runs inside the same Compose
network. For local development the game server normally runs directly on the host via
`yarn run run-server` (see root README), not inside a container, so it needs to reach the website's
container the same way a browser does — through a published host port. With only `Public` (`8000`)
published, `LiveWebsiteClient.ts`'s calls to `MASTER_API_BASE_URL` (default
`http://localhost:8001`) failed with `ECONNREFUSED`. Fixed by adding
`"containerRunArguments": "-p 8001:8001"` to the Container launch profile — this is purely a local
convenience for VS's Docker debug profile and has **no effect on the production docker-compose
file**, where `GameServerApi` must stay unpublished (see the staging/production deployment note in
§6.2 above the profiles). If a real Docker Compose–orchestrated local setup is used instead
(game server + website both as Compose services), this isn't needed — the game server would reach
`GameServerApi` via the service's internal DNS name/port directly, same as production.

**Enforcement (`Infrastructure/EndpointRoutingExtensions.cs`):** the private route groups call
`.RequireLocalPort(gameServerApiPort)`, which attaches `RequireLocalPortMetadata` rather than
enforcing anything itself. A single `app.UseLocalPortRestriction()` middleware — registered after
`UseRouting()` (so `HttpContext.GetEndpoint()` resolves) but **before** `UseAuthentication()`/
`UseAuthorization()` — reads that metadata off the matched endpoint and returns a bare 404
immediately if `HttpContext.Connection.LocalPort` doesn't match. Two deliberate design choices,
both found by testing rather than assumed:
- **`Connection.LocalPort`, not `RequireHost`.** `RequireHost("*:8001")` matches the client-supplied
  `Host` header, not the physical port a connection was accepted on — a request that connects to
  the public port but sends a forged `Host: internal:8001` header would satisfy it. `LocalPort`
  reflects the real accepting socket and can't be spoofed.
- **Plain middleware before `UseAuthentication()`, not an `AddEndpointFilter`.** Endpoint filters
  run as part of terminal endpoint invocation, which is *after* the authentication/authorization
  middleware. A filter-based first attempt at this was live-tested and found to leak the 401 Basic
  Auth challenge (proving the endpoint exists and is Basic-Auth-gated) to the public port before
  the filter ever ran. Moving the same port check into middleware ahead of `UseAuthentication()`
  closes that: a wrong-port request now gets an indistinguishable-from-nonexistent 404 with no
  `WWW-Authenticate` challenge, confirmed via a live curl test matrix (public port + private path,
  with and without a spoofed `Host` header, both 404; private port + private path without auth,
  401 as expected; both ports still serve their legitimate public routes with 200).

### 6.3 `/api/docs` — interactive OpenAPI UI for the public API only (implemented)

`Microsoft.AspNetCore.OpenApi` (the framework's own built-in generator, **not** Swashbuckle/Swagger
— see [the ASP.NET Core OpenAPI docs](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi))
is wired up in `Program.cs` to publish a single OpenAPI document at the framework's default route,
`GET /openapi/v1.json`. `Scalar.AspNetCore` (https://github.com/scalar/scalar) renders an
interactive, "try it out"-capable UI on top of that document at `GET /api/docs` — deliberately
Scalar rather than Swagger UI, per preference. Both are reachable on any configured Kestrel
endpoint. The document describes **only** `PublicApi` (`/api/public/game/{id}`) — the one JSON REST
endpoint meant for outside/third-party consumption. `PlayApi` returns HTML, not JSON, and
`UsersApi`/`GamesApi`/`RoomsApi`/`NotificationsApi` are the private, Basic-Auth-only,
port-restricted game-server contract from §6.2 that only the TS game server should ever call — both
are deliberately excluded from the generated document, not merely undocumented.

**How the filtering works:** `PublicApi.MapPublicApi()` tags its route group with
`.WithGroupName("public")`. `AddOpenApi`'s `options.ShouldInclude` is set explicitly to
`description => description.GroupName == "public"` rather than relying on the framework's default
"include endpoints with no group name too" behavior, so any future endpoint added without a group
name doesn't silently leak into `/api/docs`.

**Why the raw document stays at the default `/openapi/v1.json` route instead of `/api/docs`
itself:** the document is left **unnamed** (so it gets the framework's own default name, `"v1"`)
and mapped with a plain `app.MapOpenApi()` (no custom route). `app.MapScalarApiReference("/api/docs", ...)`
is a separate endpoint that serves the interactive UI's HTML/JS and, by default, already knows to
fetch the OpenAPI document from `/openapi/{documentName}.json` for whichever document(s) are
registered — no extra wiring needed to point it at the right JSON. (An early attempt served the raw
JSON itself directly at `/api/docs` via `MapOpenApi("/api/docs")` with an explicitly-named "public"
document; that mismatch between the document's registered name and the query-less fallback's
hardcoded expectation of `"v1"` made `/api/docs` 404 every time — found by testing, not assumed.
Switching to the default unnamed/"v1" document and letting Scalar own the `/api/docs` route instead
sidesteps that whole class of route-vs-document-name mismatch.)

**Anonymous access, matching Django:** `PublicApi`'s route group has no
`.RequireAuthorization()` at all. This matches Django's *actual* behavior — `api/views.py`'s
`get_game_view` uses `@permission_classes([])` (`AllowAny`), and its own docstring says "Public API
endpoint for anonymous access to view_of_game" — even though `api/PUBLIC_API.md` claims
"Authentication Required: Yes" (stale docs, contradicted by the code). An earlier version of this
port required a session cookie here, which was a bug: requiring auth defeats the point of a
"public" endpoint for third-party sites embedding live game state, since they have neither a
same-site session cookie to forward nor any interest in an OAuth login flow, and since a
credentialed cross-origin request also runs into CORS preflight/credentials complications that a
plain, anonymous, cookie-less GET avoids entirely. Fixed to have no authorization requirement,
matching Django.

## 7. Chat (`Snr.Web/Chat`) — WebSocket handler mirroring `consumers.py`

Implement a raw `app.Map("/ws/chat/room/{roomId}", ...)` WebSocket endpoint that:

- Requires cookie-authenticated user (equivalent to Channels' `AuthMiddlewareStack`).
- Validates room exists + user has access (public room, or a `UserInRoom` row for private rooms),
  creating a `UserInRoom` row on first connect — same as `ChatConsumer.connect`.
- Handles the same message `type`s the TS `ChatClient.ts` already sends/expects:
  `chat_message`, `chat_view_message`, `chat_retrieve`, and server → client
  `chat_message`, `chat_messages_retrieved`/`more_chat_messages_retrieved`, `connected_users`,
  `force_disconnect`.
- Re-implements the "tongueless" rate limiting (`IMemoryCache`/Redis, one message per 60s, single
  character replies only) and the private-message email notification (7-minute de-dupe window),
  using the same thresholds as `consumers.py`.
- Presence tracking (`connected_users` for the public room) via a small Redis-backed dictionary,
  replacing the Django cache-based `get_connected_users_cache_key` helpers — same stale-after-1-hour
  pruning behavior.
- Cross-instance fan-out via Redis pub/sub (`StackExchange.Redis`), replacing `channel_layer`.

Because the JSON shapes are unchanged, **`ChatClient.ts` needs no changes.**

## 8. Serving the game client (equivalent of `build_and_place_game_client_into_django.sh`)

### 8.1 One small required change in `agot-bg-game-server/`

`public/index.html` used to hardcode a **Django template tag**:

```html
<div style="display: none">
    {{ auth_data|json_script:"auth-data" }}
</div>
```

It's now a framework-neutral placeholder so any backend can inject the auth payload:

```html
<script id="auth-data" type="application/json">AUTH_DATA_JSON</script>
```

**Important, learned the hard way:** the placeholder must be literal *text* inside a real
element, not an HTML comment. An earlier version of this placeholder was
`<div style="display: none"><!--AUTH_DATA_JSON--></div>`, which looked more "neutral" but is
wrong: `webpack.client.js`/`webpack.client.local.js` build with `HtmlWebpackPlugin` in production
mode, whose default minify preset (`removeComments: true`) strips *all* HTML comments from the
built `dist/index.html` — including this one — before the file ever reaches either backend. The
placeholder would silently disappear from the real built asset (while still looking correct in
`public/index.html` and in any manual review), so `PlayApi.cs`'s string replace found nothing to
replace, and the client threw "No auth data available, can't authenticate to the server" at
runtime. The current `<script id="auth-data" type="application/json">AUTH_DATA_JSON</script>`
placeholder survives minification because it's a real, non-empty text node — and
`type="application/json"` keeps `minifyJS`/the browser from touching or executing it before the
backend substitutes real JSON in. `agot-bg-website.Tests/Api/PlayApiAuthDataPlaceholderTests.cs`
pins this contract down. If this ever needs to change again, verify against the actual *built*
`agot-bg-game-server/dist/index.html` (via `yarn run build-local-client`), not just the
`public/index.html` source.

This is the only change needed in the game-server repo for this migration.

### 8.2 `build_and_place_game_client_into_dotnet.ps1` (new script, same shape as the bash one)

```powershell
# Mirrors build_and_place_game_client_into_django.sh, targeting the .NET app instead of Django.
Write-Host "---> Building the game client"
Push-Location agot-bg-game-server
yarn install
yarn run build-local-client
Pop-Location

Write-Host "---> Placing the static files of the game client into the .NET app"
$dest = "agot-bg-website-dotnet/src/Snr.Web/wwwroot/static_game"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Recurse -Force "agot-bg-game-server/dist/*" $dest -Exclude "index.html"

$templateDir = "agot-bg-website-dotnet/src/Snr.Web/GameClientTemplates"
New-Item -ItemType Directory -Force -Path $templateDir | Out-Null
Copy-Item -Force "agot-bg-game-server/dist/index.html" "$templateDir/play.html"
```

A POSIX `build_and_place_game_client_into_dotnet.sh` twin should exist too, for parity with CI/Linux
maintainers, mirroring the existing bash script style.

### 8.3 Serving it (`PlayController`, equivalent of Django's `views.play`)

```csharp
[Authorize]
public class PlayController : Controller
{
    private static readonly Lazy<string?> Template = new(() =>
        File.Exists(TemplatePath) ? File.ReadAllText(TemplatePath) : null);

    [HttpGet("/play/{gameId:guid}/{userId:guid?}")]
    public async Task<IActionResult> Play(Guid gameId, Guid? userId)
    {
        // ... same authorization checks as Django's views.play:
        //   - banned users get force-logged-out
        //   - "on probation" users can't join new lobby games
        //   - userId param requires "can_play_as_another_player" permission
        var authData = new { userId, requestUserId = CurrentUserId, gameId, authToken = game_token };
        var html = Template.Value ?? await System.IO.File.ReadAllTextAsync(FakeTemplatePath);
        var json = JsonSerializer.Serialize(authData); // HTML-escaped the same way Django's json_script does
        return Content(html.Replace("<!--AUTH_DATA_JSON-->",
            $"<script id=\"auth-data\" type=\"application/json\">{HtmlEncoder.Default.Encode(json)}</script>"),
            "text/html");
    }
}
```

`GameClientTemplates/play_fake.html` (a straight port of Django's `play_fake.html`) is served
instead when the real template hasn't been built/placed yet, so `dotnet run` alone still boots a
usable (if game-less) website — same developer experience as today.

### 8.4 Deployment (`website.Dockerfile` equivalent)

Two-stage build, same shape as the current one:

```dockerfile
FROM node:16 AS build-client
WORKDIR /app
COPY ./agot-bg-game-server/package.json ./agot-bg-game-server/yarn.lock ./
RUN yarn install --frozen-lockfile
COPY ./agot-bg-game-server/ .
ENV ASSET_PATH=https://swordsandravens.ams3.cdn.digitaloceanspaces.com/
RUN yarn run generate-json-schemas && yarn run build-client

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-web
WORKDIR /src
COPY ./agot-bg-website-dotnet/ .
RUN dotnet publish src/Snr.Web -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build-web /app/publish .
COPY --from=build-client /app/dist ./wwwroot/static_game
COPY --from=build-client /app/dist/index.html ./GameClientTemplates/play.html
ENTRYPOINT ["dotnet", "Snr.Web.dll"]
```

## 9. Local dev workflow (replaces the relevant section of the root README)

### 9.1 Config mapping (`.env` → `appsettings`/user-secrets)

| Django `.env` key | ASP.NET Core equivalent |
|---|---|
| `DEBUG` | `ASPNETCORE_ENVIRONMENT=Development` |
| `SECRET_KEY` | Data Protection key ring (auto in dev; a persisted key in prod) |
| `DATABASE_URL` | `ConnectionStrings:Default` |
| `SOCIAL_AUTH_GOOGLE_OAUTH2_KEY/SECRET` | `Authentication:Google:ClientId/ClientSecret` |
| `SOCIAL_AUTH_DISCORD_KEY/SECRET` | `Authentication:Discord:ClientId/ClientSecret` |
| *(new)* | `Authentication:Instagram:ClientId/ClientSecret` |
| `EMAIL_HOST/PORT/HOST_USER/HOST_PASSWORD` | `Email:Host/Port/Username/Password` |
| `REDIS_URL` | `ConnectionStrings:Redis` |
| `AWS_*` (S3 static storage) | `BlobStorage:*` (optional, only if you keep S3-hosted static assets) |
| *(new, replicated from the game server's `.env`)* | `GameServer:MasterApiUsername/Password` — Basic Auth credentials the game server sends |

### 9.2 Launching the website only

```bash
cd agot-bg-website-dotnet
dotnet tool restore                     # ef core CLI, etc.
docker compose -f ../docker-compose.yml up -d   # reuse existing postgres/redis
dotnet ef database update --project src/Snr.Infrastructure --startup-project src/Snr.Web
dotnet run --project src/Snr.Web
```

Use `dotnet user-secrets` for the OAuth client id/secret pairs in development, same role as
Django's `.env` file. Since Google/Discord/Instagram auth won't work without real app credentials,
local sign-in during development uses **the new local username/password flow** — solving the
exact pain point Django had ("As Google and Discord authentication is not available you can login
via `/admin`" → now: just register locally).

### 9.3 Launching the game + website together

Same shape as today, three steps:

```bash
./build_and_place_game_client_into_dotnet.ps1
cd agot-bg-website-dotnet && dotnet run --project src/Snr.Web
cd agot-bg-game-server && yarn run run-server   # .env still has MASTER_API_* pointed at the .NET app's port
```

## 10. Data migration tool (`Snr.Migration`)

A small, **repeatable/idempotent** console app, not a one-shot script — safe to re-run against a
freshly-restored copy of the production Django DB as many times as needed while building/testing
the new site, and again at final cutover.

```
dotnet run --project src/Snr.Migration -- import --legacy "Host=...;Database=snr_django;..." --target "Host=...;Database=snr_dotnet;..."
```

Import order and behavior:

1. **Users** — copy `id, username, email, ...settings fields...` only, per your requirement
   (no auth/password/social-login data exists in Django to migrate anyway). Every imported row is
   written with `ImportedFromLegacy = true`, `Claimed = false`, `PasswordHash = null`, no
   `AspNetUserLogins` rows. Re-running updates settings fields for already-imported-but-still-
   unclaimed rows (so you can re-import right up to cutover); it never touches rows that have since
   been `Claimed` by a real login, to avoid clobbering a live user's data.
2. **Groups/Roles** — copy Django's `auth_group`/`user_groups` membership onto ASP.NET Identity
   roles of the same name.
3. **Rooms** (chat) — copy `id, name, public, max_retrieve_count`, preserving `id`.
4. **Games** — copy `id, name, owner_id, serialized_game, view_of_game, version, state,
   created_at, updated_at, last_active_at`, preserving `id`.
5. **PlayerInGame** — copy `game_id, user_id, data`.
6. **Messages** (chat) — optional/streamed in batches given likely volume; can be deferred past
   initial cutover if chat history isn't considered critical, without blocking anything else.
7. **PbemResponseTime** — copy directly, historical/statistical only.
8. **UserInRoom** — optional; can be left to be recreated naturally as users reconnect to chat
   rooms (the consumer/handler already creates these on demand), so it's fine to skip entirely.

Verification pass at the end: row counts per table compared between source and target, plus a spot
check that a sample of `Game.Id`/`User.Id`/`Room.Id` values round-trip unchanged (critical, since
those are embedded inside `serialized_game` JSON the TS server still owns).

### 10.1 Historical backfill of `PreviousPlayerInGame`

Unlike every other table above, this one **cannot** be populated by a straight column copy — the
legacy DB never stored it, and computing it requires replaying `GameLogManager.logs` (§4.4), whose
shape is owned by `agot-bg-game-server`, not `Snr.Migration`. **This is fully feasible, not a hard
gap**: the serialized game has tracked player-vote-out/replacement and clock-timeout-replacement
log entries for ~3 years now, so every historical game since then has enough information in
`serialized_game.logs` to reconstruct who was removed, when, and why — this backfill script just
hasn't been *written* yet. So this step deliberately does not live in the .NET importer:

```
# run once, from agot-bg-game-server, against a restored copy of the production Django DB
npx ts-node scripts/backfillPreviousPlayers.ts \
  --legacy "postgres://.../snr_django" \
  --target "postgres://.../snr_dotnet"
```

- Connects **read-only** to the legacy DB, streams every `Game` row that has a non-null
  `serialized_game` (regardless of `state`, so `CANCELLED`/abandoned games are included too — cheap
  to include, and their `PreviousPlayerInGame` rows are simply unused if such games stay excluded
  from win-rate scope per §10.2).
- Reuses the game server's own `serializedGameMigrations` / `GlobalServer`-style version-migration
  pipeline (already battle-tested for loading arbitrarily old saved games) to deserialize each blob
  via `EntireGame.deserializeFromServer`, exactly as if the game were being resumed.
- Calls the new `getPreviousPlayersInGame()` (§6.1) on each deserialized game.
- Upserts rows into the new DB's `PreviousPlayerInGame` table, keyed on `(GameId, SequenceNumber)`
  — safe to re-run, e.g. re-run once immediately before final cutover alongside step 4 of §11.
- Runs once per game entirely in-process (no website/API round-trip needed), so it can run
  standalone at any time before or independent of the main `Snr.Migration` import.

### 10.2 Win-rate calculation (new formula)

- **Wins**: `PlayerInGame` rows where `is_winner = true`, for games with `state = FINISHED` (same
  scope Django uses today).
- **Losses**: `PlayerInGame` rows where `is_winner = false` (`FINISHED` games) **plus every**
  `PreviousPlayerInGame` row for `FINISHED` games — counted as a loss unconditionally, regardless of
  `WasWinner`, per your instruction that being removed from a game should never count in your favor
  even if your former house went on to win without you. `WasWinner` is stored anyway, purely for
  potential future analytics (e.g. "how often does the replacing vassal/player go on to win").
- `CANCELLED`/`IN_LOBBY`/`ONGOING` games contribute to neither wins nor losses, matching today's
  Django behavior of only scoring `FINISHED` games.

## 11. Rollout plan

1. Stand up `agot-bg-website-dotnet` against a separate `snr_dotnet` database/port, Django keeps
   running unmodified against its own DB.
2. Point a staging copy of the game server (`MASTER_API_BASE_URL`) at the new app; run the importer
   against a restored production snapshot; smoke-test login (all 3 OIDC providers + local),
   settings page, creating/joining a game, chat.
3. Verify the "claim an imported account" flow end-to-end for at least one user per provider.
4. Cut over DNS/reverse proxy to the .NET app; re-run the importer one final time immediately
   before cutover to catch any last-minute Django writes.
5. Keep Django + its DB read-only and available for a rollback window before decommissioning.

## 12. Open questions / risks to confirm before implementation

- **Instagram email availability** — see §5.2; may need a "confirm your email" nudge screen since
  the provider often won't supply one. Also requires a Meta developer app + app review for the
  "Instagram API with Instagram Login" product before it works for non-test users.
- **Static asset hosting** — Django currently serves `static_game`/`static` either via WhiteNoise
  (dev) or S3 (prod, `django-storages`). Decide whether ASP.NET Core continues serving
  `static_game` directly (simplest, mirrors dev behavior) or keeps the S3/CDN split used in
  production today (`ASSET_PATH` env var already supports a CDN prefix — no change needed there
  either way).
- **Hosting/process model** — Django currently runs under `gunicorn` with a `gunicorn_config.py`
  hook for `django_prometheus` multiprocess mode; ASP.NET Core's built-in Kestrel host replaces
  this outright, no multiprocess metrics registry needed.
- **Vanilla Forum integration** — referenced in Django settings/migrations but effectively
  disabled (`VANILLA_FORUM_API_KEY` optional, the actual account-creation call was removed). Decide
  whether to carry the dead integration points forward or drop them in the new codebase.
- **Debug tooling** — `django-debug-toolbar` (superuser-only) has no exact ASP.NET Core
  equivalent; consider `MiniProfiler` for local development only.
- **`PreviousPlayerInGame` scope** — this is a deliberate *improvement* over Django (which never
  tracked player-removal history at all), not a risk from the migration itself. Two scope points
  worth a quick explicit confirmation before implementation: (1) whether the historical backfill
  (§10.1) should walk `CANCELLED` games too (currently: yes, included, but excluded from win-rate
  scope like today) and (2) whether win-rate should stay `FINISHED`-only as assumed in §10.2, or be
  widened now that removal tracking exists.

## 13. Future improvements (out of scope for this migration, noted for later)

These are deliberately **not** part of the Django→.NET migration itself — noted here so they don't
get lost, to be picked up as separate follow-up work once the migration is live:

- **Precomputed statistics tables.** Win rate and average PBEM response time are currently
  calculated on the fly whenever a user profile page is visited (same as Django does today). Now
  that we have a clean EF Core schema, add a `PlayerStatistics` table (one row per user, or per
  user+game-mode) that's updated incrementally whenever a game finishes (in the same transaction
  that sets `Game.State = FINISHED` / writes `PlayerInGame.IsWinner`), rather than recomputed from
  scratch per profile view.
- **Public game statistics.** With a real relational schema (vs. Django's `view_of_game` JSON
  blobs), it becomes easy to add a public stats page/API answering things like "which house wins
  which game mode most often, broken down by settings (PBEM vs. live, Mother of Dragons on/off,
  player count, etc.)". Needs a bit of design work on what "game mode"/"settings" should mean as
  stable, queryable dimensions (today they're free-form JSON inside `ViewOfGame.settings`) — likely
  its own `GameStatistics`/`HouseWinBySettings` summary table(s), populated the same
  incrementally-on-finish way as `PlayerStatistics` above.
- **Extended Admin entity browsing & chat moderation.** The initial Admin area provides User
  search/ban/role-assignment and Game search/raw-JSON-edit. As a follow-up, expand the Admin panel
  to support browsing and editing across all database entities:
  - Chat rooms and messages: browse active/historical rooms, search chat messages, delete/moderate
    inappropriate messages.
  - User details: edit user profile fields (ProfileText, MuteGames, settings flags) directly from
    admin UI.
  - PBEM response times, user room memberships, and previous players in game inspection.
- **UI library / dark Game-of-Thrones theme.** The current Razor Pages UI still uses whatever
  Identity's default scaffolded markup looks like. Recommendation: **Tailwind CSS + DaisyUI**
  rather than Bootstrap or a Material component kit — it's the most-recommended current pick for
  server-rendered Razor Pages (no SPA framework lock-in, works fine with plain `.cshtml`), gives
  full utility-first control over a custom dark palette/typography instead of fighting a
  Bootstrap-flavored default look, and DaisyUI's theming system (CSS custom properties) makes it
  straightforward to build one cohesive "Swords and Ravens" dark/parchment-accented theme instead
  of a generic admin-dashboard look. (Commercial alternative if a fully polished component suite
  is preferred over hand-rolling every component: Telerik UI for ASP.NET Core, which also ships a
  visual theme builder for custom dark themes.)

## 14. Admin area, nav, and auth-flow hardening (implemented)

Follow-up work after first getting the app running locally end-to-end:

- **Email confirmation is now always required** (`options.SignIn.RequireConfirmedAccount = true`
  unconditionally in `Program.cs`, no longer skipped in Development) so local testing exercises the
  same confirm-email gate production has. `Login.cshtml.cs` now gives a specific message for the
  `SignInResult.IsNotAllowed` case (unconfirmed email) instead of a generic "Invalid login attempt"
  — that generic message was the actual root cause of "I logged in but the nav still shows
  Login/Register": the sign-in was silently refused, not a caching/rendering bug.
- **`options.User.RequireUniqueEmail = true`** — two `ApplicationUser`s can no longer share an
  email, which both `Register.cshtml.cs` and `ExternalLogin.cshtml.cs` now rely on.
- **Register with an OAuth-linked email is forbidden**, with a clear message telling the visitor to
  sign in with the linked provider instead (and that they can add a password afterwards from
  `Manage/SetPassword`, already provided by scaffolded Identity UI).
- **External login auto-links by email** instead of erroring/duplicating: `ExternalLogin.cshtml.cs`
  now calls `AccountLinkingService.TryLinkByEmailAsync` before creating a new user; both the
  legacy-import "Linked" outcome and a plain "already claimed" existing account are treated as
  "add this login to that existing user" for this flow specifically (the service's own
  `ConflictAlreadyClaimed` semantics are otherwise unchanged/still tested for the legacy-migration
  use case in `AccountLinkingServiceTests`).
- **Phone number field removed** from `Manage/Index.cshtml` (unused, never collected by Django).
- **Nav rebuilt to match Django's production bar**: All Games / My Games (signed-in only) / Rules /
  About / FAQ (external link) / Admin (Admin role only), replacing the scaffolded Home/Privacy
  links. New pages: `Pages/Games.cshtml` (open + ongoing games), `Pages/MyGames.cshtml`
  (`[Authorize]`, games the current user is a player in), `Pages/Rules.cshtml`, `Pages/About.cshtml`
  (static content mirroring Django's `rules.html`/`about.html`).
- **New `Areas/Admin` Razor Pages area** — the .NET equivalent of Django Admin (ASP.NET Core has no
  built-in one). Gated by an `"AdminArea"` authorization policy (`RequireRole(RoleNames.Admin)`)
  applied via `options.Conventions.AuthorizeAreaFolder("Admin", "/", "AdminArea")`. Provides:
  - `Admin/Users` — search by username/email/id, ban/unban (toggles the `Banned` role and bumps the
    security stamp so it takes effect immediately), and `Admin/Users/Edit` for full role editing.
  - `Admin/Games` — search by name/id, and `Admin/Games/Edit` for viewing/editing the raw
    `SerializedGame`/`ViewOfGame` JSON directly and cancelling a game — the direct equivalent of
    what this maintainer previously had to do by hand-editing the Django database.
  - Note the same caveat that applied in Django: editing `SerializedGame` only sticks if the game
    server doesn't have that game loaded in memory (it will overwrite on its next save).
- **Real SMTP vs. smtp4dev**: smtp4dev (already wired up per §6/§9) is a local catcher — it really
  receives the SMTP transaction so confirmation/reset emails show up in its web UI at
  `http://localhost:5099`, but it never delivers anywhere else by design, so it's normal to not see
  mail in a real inbox while using it. To test genuine end-to-end delivery, point `Email:Host`
  (and `Email:Port`/`Email:Username`/`Email:Password`/`Email:EnableSsl` if your provider needs them)
  at a real SMTP relay via `dotnet user-secrets set Email:Host ...` etc. (never commit real
  credentials — see README.md "Email" section for the exact commands and provider notes).
- **Account deletion ("Took the Black" soft-delete), implemented.** In Django, account deletion was
  never supported because deleting a user broke `PlayerInGame` FK joins/rendering for past games.
  `PlayerInGame`/`PreviousPlayerInGame`/`Message` all reference `AspNetUsers` with
  `ON DELETE RESTRICT`, so a real row delete (or moving the row to a separate `DeletedUsers` table
  and dropping it from `AspNetUsers`) would either be blocked outright or require rewriting every
  historical game/chat FK to a second table. Instead we keep the `AspNetUsers` row and soft-delete
  it in place via `Services/AccountDeletionService.cs`:
  - Adds `ApplicationUser.IsDeleted`/`DeletedAt`, and a `[NotMapped]` `DisplayName` property that
    returns `"Took the Black"` whenever `IsDeleted` is true (and the real `UserName` otherwise).
    Every place a username used to be rendered (`Games`/`MyGames`/`Admin/Games` owner column,
    `Admin/Users` list) now reads `DisplayName` instead of `UserName` directly.
  - On deletion: removes all roles/external logins/claims, disables 2FA, nulls `Email`/
    `NormalizedEmail`/`PasswordHash`/`ProfileText`/`LastWonTournament`, regenerates `GameToken` (a
    `NOT NULL UNIQUE` column, so it can't be nulled), sets `LockoutEnabled`/`LockoutEnd` to
    permanently lock the row out (belt-and-braces, on top of the password already being gone), and
    rotates `SecurityStamp` to invalidate any existing auth cookie immediately.
  - `UserName`/`NormalizedUserName` can't be nulled either — Identity's default `UserValidator`
    rejects a null/empty username regardless of `RequireUniqueEmail`, and still enforces its own
    uniqueness check — so they're replaced with the user's own (already-unique, non-PII) `Id`
    instead. This frees the real email and username up for someone else to register with again,
    while `DisplayName` still shows the same `"Took the Black"` text for every deleted account.
  - Self-service via `Areas/Identity/Pages/Account/Manage/DeletePersonalData.cshtml(.cs)` (now
    calls `AccountDeletionService` instead of the Identity-scaffolded `UserManager.DeleteAsync`),
    and admin-triggered via a new "Delete" button in `Admin/Users/Index`.
  - `Login.cshtml.cs` also picked up a related fix here: it used to call
    `PasswordSignInAsync(Input.Email, ...)`, which only worked because `UserName` used to always
    equal `Email`. Now that users can pick an independent username, it looks the user up by email
    via `UserManager.FindByEmailAsync` first and signs in with the resolved `ApplicationUser`.
  - The public user-profile page below correctly returns 404 for `IsDeleted` users. Nothing else
    needed to change: `PlayerInGame`/`PreviousPlayerInGame` resolve normally since the row (and its
    `Id`) still exists, they'll just join to a `DisplayName` of `"Took the Black"`.

- **Public user-profile page, implemented** (`Pages/User.cshtml(.cs)`, route `/User/{id:guid}`,
  mirroring Django's `agotboardgame_main.views.user_profile` / `user_profile.html`):
  - Returns 404 for a nonexistent or `IsDeleted` user (see above).
  - Shows role badges (same color mapping as Django's `settings.GROUP_COLORS`), joined date, last
    activity (via new `Infrastructure/RelativeTimeFormatter.cs`, a hand-rolled port of Django's
    `naturaltime` filter), ongoing/finished/won/replaced-by-vote-or-timeout counts, win rate, and
    average PBEM response time.
  - Win rate reuses the previously-built (and until now unused) `WinRateCalculator.Calculate`
    unchanged. Games are read from `PlayerInGame.Data` (`{house, is_winner}`, snake_case — matches
    the TS game server's `EntireGame.ts` wire format exactly) and `Game.ViewOfGame`
    (`{turn, maxPlayerCount, waitingFor, winner, settings: {setupId, faceless, pbem}}`,
    camelCase). Faceless games (`settings.faceless == true`) are excluded from the games list
    entirely; `setupId == "learn-the-game"` games are excluded only from the win-rate numerator/
    denominator (Django's `.exclude(data__is_winner__isnull=True)` equivalent), still shown in the
    list. A `PreviousPlayerInGame` row for a `Finished` game always counts as an unconditional loss.
  - Average PBEM response time: new `Services/PbemResponseTimeCalculator.cs`, a pure/testable port
    of Django's exact algorithm — take the most recent 100 `PbemResponseTime.ResponseTime` values,
    and if there are more than 20, sort and drop the 10 fastest/10 slowest before averaging.
  - House icons: copied the 8 simply-named Django `static/house_icons/{house}.png` files into
    `wwwroot/house_icons/` — the .NET game-client's webpack-bundled `static_game/*.png` files have
    content-hashed names, so they aren't usable for a direct by-house-name `<img>` lookup.
  - Owner/username columns in `Games`/`MyGames`/`Admin/Games`/`Admin/Users` now link to the
    profile, and the nav's account dropdown got a "Profile" link alongside the existing "Manage"
    one. Gotcha to remember for future numeric-display work: string interpolation of a `decimal`/
    `double` with a `:F1`-style format specifier uses the server's current culture (so e.g. a
    German-locale host would render `33,3 %` instead of `33.3 %`) — always format via
    `.ToString("F1", CultureInfo.InvariantCulture)` instead for any value shown to users.

- **CoreAdmin comparison tab, implemented.** Added the `CoreAdmin` NuGet package alongside the
  hand-built `Areas/Admin` area (kept, not replaced — see its doc comment/nav entry) purely so the
  maintainer can compare the two side by side. Known CoreAdmin limitation: it cannot edit `jsonb`
  columns (`Game.SerializedGame`/`ViewOfGame`), so the hand-built `Admin/Games/Edit` raw-JSON editor
  remains the only way to edit those. Longer-term idea (not started): reuse the hand-built admin
  panel's UI/permissions model so High Members (not just Admins) can use a subset of it.
- **Dead `PhoneNumber`/`PhoneNumberConfirmed` fields, fixed.** These were still visible via
  CoreAdmin and in the "download my private data" JSON export despite the `RemovePhoneNumberField`
  EF migration having already dropped the columns — the migration only removed the *database*
  columns, but `ApplicationUser` still inherited them from `IdentityUser<Guid>` in memory (EF Core's
  reflection-based CoreAdmin scaffolding and the data-export code both read off the CLR type, not
  the DB schema). Fixed by shadowing both with `internal new` no-op properties on `ApplicationUser`.
- **`GameStateColumnRight` rename, implemented.** The user setting that used to be named
  `UseResponsiveLayoutOnMobile` (legacy/unused Django name, never actually wired to a Django
  migration) is now really used for "load the game with the game-state column aligned to the
  right", so the EF Core column/property was renamed via a data-preserving `RenameColumn`
  migration. The public user-settings API still emits/accepts the JSON key
  `use_responsive_layout_on_mobile` (via `[JsonPropertyName]`) so the TypeScript game server needs
  no corresponding change.
- **Enhanced Games/MyGames lists, implemented** (`Services/GameListing/GameListQueryService.cs` +
  `Pages/Shared/_GamesTable.cshtml`, mirroring Django's `views.py`/`views_helpers.py`
  `games()`/`enrich_games()`): round/waiting-for tooltip, live/PBEM badge, house icon +
  your-turn/playing badge, unread public/private message badges (batched into 2 queries total,
  not one per room), replacement-needed badge + admin "Join as ..." action, password-protected
  lock badge, X/Y player count, and four extra lists (games waiting for inactive players /
  without a move for 5 days / inactive tournament games / inactive private games), gated behind
  the same `CanPlayAsAnotherPlayer`/`CanCancelGame` permissions as the existing admin features.
  Every query projects into a DTO that never selects `SerializedGame` (the .NET equivalent of
  Django's `.defer('serialized_game')`, done to avoid the same OOM issue that pattern was
  originally added for). **EF Core gotcha found here**: `OrderBy`/`Take` must be applied to the
  `IQueryable<Game>` *before* the record-projecting `.Select(...)`, not after — ordering after a
  projection containing a nested collection subquery (`Players.Select(...).ToList()`) fails to
  translate against Npgsql, even though it silently "works" against the EF Core InMemory provider
  used by this repo's unit tests. Always spot-check new EF Core projection code against the real
  `snr_dotnet` Postgres dev database, not just the InMemory-provider test suite.

## 15. Roadmap / follow-ups (as of 2026-09-01)

Kept up to date here so a fresh session can answer "what's next" immediately without having to
re-derive it. Update this section whenever priorities shift or an item is completed.

### Before go-live (blocking rollout, see §11/§12)
1. **Instagram OIDC** — needs a Meta developer app + app review for the "Instagram API with
   Instagram Login" product before it works for real (non-test) users; start the review process
   early, it can take days/weeks.
2. **Full data-migration dry run** — restore a production DB snapshot, run `Snr.Migration` against
   it, and smoke-test end-to-end: login via all 3 OIDC providers + local, the account-claiming
   flow, chat, and game create/join — not just spot-checked tables.
3. **Static asset hosting decision** — serve `static_game` directly from Kestrel (simplest) vs.
   keep the S3/CDN split Django uses in production (`ASSET_PATH` already supports a CDN prefix
   either way, so this is low-risk to defer).
4. **Two small open questions from §12** — should `PreviousPlayerInGame` backfill include
   `CANCELLED` games, and should win-rate stay `FINISHED`-only now that removal tracking exists.

### Deferred / nice-to-have (see §13), suggested rough order
1. **Precomputed `PlayerStatistics` table** — win-rate/PBEM response time are recomputed on every
   profile view today; update incrementally when a game finishes instead.
2. **Public game statistics page** — house win-rates by game mode/settings, now that data is
   relational instead of Django's free-form `ViewOfGame.settings` JSON.
3. **Extended Admin: chat moderation** — browse/search/delete chat messages, edit user profile
   fields (`ProfileText`, `MuteGames`, settings flags) directly from the admin UI.
4. **CoreAdmin → hand-built admin reuse for High Members** — expose a subset of the hand-built
   `Areas/Admin` UI/permissions to High Members (not just Admins), once it's more battle-tested.
5. **UI theme pass** — Tailwind/DaisyUI dark "Swords and Ravens" theme instead of today's default
   Identity-scaffold look, once functionality is stable.

### Already done, no longer on the list
Pagination for Admin Users/Games/Rooms/Messages; CoreAdmin comparison tab; dead phone-number field
cleanup; `GameStateColumnRight` rename; enhanced Games/MyGames lists with badges + inactive-game
lists (all documented under §14 above); private game-server API split onto its own internal-only
Kestrel endpoint, not just Basic Auth (see §6.2).

