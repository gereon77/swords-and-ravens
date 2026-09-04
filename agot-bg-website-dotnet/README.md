# Swords and Ravens — ASP.NET Core website (`agot-bg-website-dotnet`)

This is the ASP.NET Core replacement for the Django `agot-bg-website`. It serves the same role in
the overall system: it owns user accounts/authentication, game records, rooms, and the generated
game-host page, and talks to the TypeScript `agot-bg-game-server` over a private REST API (and
receives WebSocket chat traffic proxied through Redis pub/sub).

See `MIGRATION_PLAN.md` in this folder for the full design rationale, and
`LOCAL_DEV_VERIFICATION.md` for a running log of what has been manually verified against live
Postgres/Redis/SMTP.

## Solution structure

| Project | Purpose |
|---|---|
| `agot-bg-website/` | The ASP.NET Core app itself: Razor Pages UI (top nav: All Games / My Games / Rules / About / FAQ / Admin), Identity (username/password + Google/Discord/Facebook OIDC), a Django-Admin-style `Areas/Admin` Razor Pages area (user search/ban/roles, raw game JSON view/edit — gated by the `Admin` role, see `MIGRATION_PLAN.md` §14), Minimal API groups under `Api/` (`GamesApi`, `RoomsApi`, `UsersApi`, `PublicApi`, `NotificationsApi`, `PlayApi`, `ChatWebSocketApi`), and `Infrastructure/` (chat, email, website-client contract implementation for the game server). `ClientAssets/` is a small npm project (Tailwind CSS + DaisyUI) that builds `wwwroot/css/app.css`. |
| `agot-bg-website.Data/` | EF Core `DbContext`, entities, and migrations — the persistence layer, shared by the website and by `Snr.Migration`. |
| `Snr.Migration/` | One-off console tool that imports the legacy Django database (users, games, rooms, ...) into the new schema. Not part of the running website. |
| `agot-bg-website.Tests/` | xUnit test project covering domain/service logic. |

## Prerequisites

- .NET SDK matching `agot-bg-website/agot-bg-website.csproj`'s `<TargetFramework>` (currently
  `net10.0`).
- Docker Desktop, for Postgres + Redis (+ optionally smtp4dev for local email testing).
- Node.js 16.x and Yarn — only needed if you also want to build the real game client (see below);
  the website runs fine without it and falls back to `GameClientTemplates/play_fake.html`.

## Running locally

1. **Start Postgres, Redis, and smtp4dev** from the repository root:

   ```powershell
   docker compose up -d db redis smtp4dev
   ```

   `db` listens on `127.0.0.1:5432` (user `postgres`, password `example`, matching
   `appsettings.json`'s `ConnectionStrings:DefaultConnection`, database `snr_dotnet`). `redis`
   listens on `127.0.0.1:6379`. `smtp4dev` is a local SMTP catcher — see "Email" below.

   > **Windows/Docker Desktop note:** connection strings in this repo use `127.0.0.1` rather than
   > `localhost` on purpose. On Windows, `localhost` often resolves to the IPv6 loopback (`::1`)
   > first, and Docker Desktop's port-forwarding proxy for published ports can intermittently
   > refuse IPv6 loopback connections with `SocketException: Cannot assign requested address`,
   > surfacing as an EF Core "transient failure" on startup (role/room seeding). If you hit that,
   > double check `ConnectionStrings:DefaultConnection`/`ConnectionStrings:Redis` in
   > `appsettings.json` and any `Email:Host` user-secret are `127.0.0.1`, not `localhost`.

   > **Visual Studio launch profiles:** there are two debug targets in the F5 dropdown — `http`
   > (runs `dotnet run` natively on Windows; uses `127.0.0.1` as above) and
   > `Container (Dockerfile)` (builds/runs the app inside its own Docker container, closer to the
   > production build). Inside that container, `127.0.0.1`/`localhost` refer to the container
   > itself, not the host, so the `Container (Dockerfile)` profile instead points at
   > `host.docker.internal` (Docker Desktop's DNS name for the host machine) via environment
   > variable overrides already set in `Properties/launchSettings.json` — no extra setup needed,
   > just pick either profile from the dropdown.

2. **Apply EF Core migrations** (creates the `snr_dotnet` database and schema). This uses the
   `dotnet-ef` global tool (`dotnet tool install --global dotnet-ef` if you don't have it yet):

   ```powershell
   cd agot-bg-website-dotnet
   dotnet ef database update --project agot-bg-website.Data --startup-project agot-bg-website
   ```

3. **Configure local secrets** (OIDC client secrets, SMTP credentials, etc.) via
   [`dotnet user-secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets) from
   `agot-bg-website/` — **never** put real credentials in `appsettings.json`, since that file is
   committed. At minimum, for local email testing against smtp4dev:

   ```powershell
   cd agot-bg-website
   dotnet user-secrets set "Email:Host" "127.0.0.1"
   dotnet user-secrets set "Email:Port" "2525"
   dotnet user-secrets set "Email:EnableSsl" "false"
   dotnet user-secrets set "Email:FromAddress" "no-reply@swordsandravens.local"
   ```

   View captured emails at http://localhost:5099. To test Google/Discord/Facebook sign-in
   locally you'll also need your own OAuth app credentials, set the same way (`Authentication:Google:ClientId` / `:ClientSecret`, etc. — see `MIGRATION_PLAN.md` for the OIDC provider setup notes).

   **smtp4dev never delivers to a real inbox — that's by design** (it's a local catcher so no real
   mail server/credentials are needed for day-to-day dev). If you want to actually test the
   end-to-end delivery experience (e.g. what the confirm-email flow feels like landing in a real
   inbox), point `Email:*` at a real SMTP relay instead, using your own credentials — **type the
   password directly into the `dotnet user-secrets set` command yourself** rather than pasting it
   into chat/a file, since user-secrets are stored unencrypted in your local profile
   (`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`) but at least never get committed:

   ```powershell
   cd agot-bg-website
   dotnet user-secrets set "Email:Host" "smtp.gmail.com"
   dotnet user-secrets set "Email:Port" "587"
   dotnet user-secrets set "Email:EnableSsl" "true"
   dotnet user-secrets set "Email:Username" "yourname@gmail.com"
   dotnet user-secrets set "Email:Password" "<an app password, not your normal account password>"
   dotnet user-secrets set "Email:FromAddress" "yourname@gmail.com"
   ```

   For Gmail, generate an [App Password](https://myaccount.google.com/apppasswords) (requires
   2-Step Verification) rather than using your normal account password — plain-password SMTP auth
   is disabled by Google for third-party apps. Any other SMTP relay you have credentials for (a
   personal domain's mailbox, Mailtrap, SendGrid, etc.) works the same way — just fill in its
   host/port/username/password.

   **API-based sending (preferred in production) vs. SMTP vs. no config at all:** exactly one
   `IEmailSender` is registered at startup (see `Program.cs`), chosen by which `Email:*` config is
   present:
   - `Email:Api:Key` set → `ApiEmailSender`, which posts to an HTTP email API (defaults to
     [Resend](https://resend.com)'s; override the base URL with `Email:Api:Host` to use a
     different API-based provider). Preferred over SMTP when both are configured, since it isn't
     subject to outbound SMTP port blocking on some hosts. Resend's free tier (3,000 emails/month,
     100/day) comfortably covers this project's volume; paid tiers start at $20/month for 50,000.
     Other similar options: Postmark, SendGrid, Mailgun, Brevo — swapping providers only needs a
     new `ApiEmailSender`-style class plus a `Program.cs` registration change, not a redesign.
   - Otherwise, `Email:Host` set → `SmtpEmailSender` (the smtp4dev/Gmail/etc. setup above).
   - Otherwise (no `Email:*` configured at all, e.g. a fresh checkout before either of the above
     steps) → `LoggingEmailSender`, which just logs what would have been sent instead of crashing
     or silently dropping the email — nothing further to configure for basic local dev.

4. **Run the website**:

   ```powershell
   cd agot-bg-website
   dotnet run
   ```

   By default this listens at **http://localhost:8000**. Both the normal project profile and the
   Visual Studio Docker profile pin the host port to 8000 in `Properties/launchSettings.json`
   (the container itself also listens on port 8000, and 8001 for the internal game-server API —
   see `appsettings.json`'s `Kestrel:Endpoints` and MIGRATION_PLAN.md §6.2). This deliberately
   matches the old Django development URL; the game server's `MASTER_API_BASE_URL` must point at
   the *8001* port instead (`http://localhost:8001/api`), since the private, Basic-Auth-only API
   routes are no longer reachable on 8000 — see `agot-bg-game-server/.env.dev.live`. You can still
   pass `--urls "http://localhost:<other-port>"` to override the project profile temporarily.
   Without the real game client built (see next section), `/play/<gameId>` serves the placeholder
   `GameClientTemplates/play_fake.html` so the rest of the site (registration, login, rooms, game
   list) can still be exercised end-to-end.

5. **Run the tests**:

   ```powershell
   dotnet test
   ```

## Building the UI (Tailwind CSS + DaisyUI)

The dark "Swords and Ravens" theme is built with Tailwind CSS v3 + DaisyUI v4 (kept on the v3/v4
generation because this environment's Node is pinned to 16.x for `agot-bg-game-server`, and
Tailwind v4 requires Node 20+). The compiled `wwwroot/css/app.css` is a **committed build
artifact**, similar to the older `wwwroot/css/site.css` — you only need to rebuild it if you change
the theme or add new component styles:

```powershell
cd agot-bg-website/ClientAssets
npm install
npm run build     # one-off build, minified, writes ../wwwroot/css/app.css
npm run watch      # rebuilds on change, for active theme/markup work
```

`tailwind.config.js` scans `../Pages/**/*.cshtml` and `../Areas/**/*.cshtml` for class usage and
defines the custom `swordsandravens` DaisyUI theme. Because the ASP.NET Core Identity UI is
scaffolded Bootstrap 5 markup (`Areas/Identity/Pages/**`, 40 files), `ClientAssets/src/app.css`
also contains a `@layer components` compatibility shim that maps every legacy Bootstrap-only class
name still used there (`form-control`, `row`/`col-md-*`, `text-danger`, ...) to its DaisyUI/Tailwind
equivalent, so those pages inherit the dark theme without being rewritten by hand. Hand-written
pages (`Pages/Shared/_Layout.cshtml`, `Index.cshtml`, `Privacy.cshtml`, `_LoginPartial.cshtml`,
`_CookieConsentPartial.cshtml`) use DaisyUI/Tailwind classes directly.

## Building and serving the real game client locally

In production, the React/MobX game client (`agot-bg-game-server`) is compiled and its static
assets are served by this website, exactly like Django did — see `Dockerfile` in this project,
which mirrors `website.Dockerfile`'s multi-stage build. For local development, the equivalent of
the repo-root `build_and_place_game_client_into_django.sh` is:

```powershell
# from the repository root (D:\_snr)
.\build_and_place_game_client_into_dotnet.ps1
```

(or `./build_and_place_game_client_into_dotnet.sh` on Linux/macOS). This script:

1. Runs `yarn install` + `yarn run build-local-client` inside `agot-bg-game-server/`.
2. Copies `agot-bg-game-server/dist/*` (excluding `index.html`) into
   `agot-bg-website-dotnet/agot-bg-website/wwwroot/static_game/`.
3. Copies `agot-bg-game-server/dist/index.html` to
   `agot-bg-website-dotnet/agot-bg-website/GameClientTemplates/play.html`.

After running it, restart `dotnet run` — `PlayApi` (`Api/PlayApi.cs`) will now serve the real
`play.html` template (with the game server's static JS/CSS from `wwwroot/static_game/`) instead of
falling back to `play_fake.html`. This mirrors how `build_and_place_game_client_into_django.sh`
worked for the Django app: it's a manual step for local dev, not part of `dotnet build`/`dotnet
run`, so you only need to re-run it when the game client changes.

> **Why the bundle is requested from `/static/...` but the files live in `wwwroot/static_game/`:**
> `webpack.client.local.js` hardcodes `output.publicPath = "/static/"` (matching Django's
> `STATIC_URL`, since that webpack config is shared between both website rewrites), so the
> generated `play.html` always references absolute URLs like `/static/bundle.<hash>.js` no matter
> which backend serves it. `Program.cs` re-exposes `wwwroot/static_game/` under the `/static`
> request path (a second `app.UseStaticFiles(...)` alongside the default `MapStaticAssets()` call)
> specifically so those URLs resolve without having to fork the webpack config. If you ever see a
> 404 for `/static/bundle.*.js`, check that `wwwroot/static_game/` actually contains the built
> assets (re-run the build script above) rather than assuming the routing is wrong.

## Migrating data from the legacy Django database

`Snr.Migration` is a console tool that reads directly from the legacy Django Postgres database and
imports users (linking by e-mail to preserve OIDC-only accounts), groups/roles, rooms, games,
`PlayerInGame`, chat messages, and PBEM response times into the new schema. It's idempotent (safe
to re-run). See `MIGRATION_PLAN.md` §7/§10 for the entity-mapping details and the
`PreviousPlayerInGame` backfill plan (planned to be reconstructed from the serialized game's
replacement/vote-out log via a future `agot-bg-game-server/scripts/backfillPreviousPlayers.ts` —
not yet written, since that data has been tracked in `serialized_game` for years and just needs a
script to replay it — see §10.1). Run the importer with:

```powershell
dotnet run --project Snr.Migration -- import --legacy "<legacy Django db connection string>" --target "<new snr_dotnet db connection string>"

# or, to only check without writing anything:
dotnet run --project Snr.Migration -- verify --legacy "<legacy Django db connection string>" --target "<new snr_dotnet db connection string>"
```
