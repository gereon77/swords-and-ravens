# Swords and Ravens

"Swords and Ravens" is a reimplementation of the board game published by Fantasy Flight Games "A Game of Thrones: Board Game - Second Edition".

This repository also serves as a bug tracker. Head to [the issues section](../../issues) if you want to report a bug, see the progression on the different features, or to see what's planned for the game! Before creating an issue to report a bug or propose a feature, please make sure that an issue does not already exist by using the search functions.

Suggestions, remarks and other feedbacks can done on [the discord server](https://discord.gg/wWgCdvM) (Ask Tex for access to the feedback channel).

## General Architecture

The project is separated into 2 components:

* A website in `C#` with `ASP.NET Core` (Razor Pages + Minimal API). This component handles user
  registrations/authentication, creating games as well as joining them. It exposes a private REST
  API used by the game server, and a WebSocket endpoint for the in-game chat (backed by Redis
  pub/sub for fan-out across instances). The code is located in `agot-bg-website-dotnet/`. This is
  the only website — the previous Django implementation has been fully retired.
* A game server in `Typescript` with `React`, `mobx` and `bootstrap`. It runs the games of AGoT. It is itself composed of a front-end and a back-end. The code is located in `agot-bg-game-server/`.

Additional documentation about how those components work can be found in the folder of each
component. In particular, `agot-bg-website-dotnet/README.md` covers the website in much more
detail than this file, and `agot-bg-website-dotnet/MIGRATION_PLAN.md` documents the full design
rationale (including the mapping from the old Django concepts to their ASP.NET Core equivalents).

## How to Run

There are multiple ways to run the code, depending on what you want to run.

### Launching the Game Only

Requires Node.js LTS (currently 24.x) and `yarn`. Install the dependencies and initialize the environment variables by executing:

```bash
cd agot-bg-game-server/
yarn install
yarn run generate-json-schemas
cp .env.dev.local .env
```

In 2 different terminals, execute:

* `yarn run run-client`
* `yarn run run-server`

Open `http://localhost:8080/static/#1` in your browser. Additional players can be simulated by opening new browser tabs and changing the number at the end of the url.

Closing and re-reunning `run-server` will create a new game.

**Note**: The chat, which is managed by the website, will not be available.

### Launching the Website Only

Requires `Docker` (for Postgres/Redis/smtp4dev) and the .NET SDK matching
`agot-bg-website-dotnet/agot-bg-website/agot-bg-website.csproj`'s `<TargetFramework>` (currently
`net10.0`). Runs natively on Windows/macOS/Linux — no WSL2/Linux-only requirement, unlike the old
Django setup.

1. **Start Postgres, Redis, and smtp4dev** (a local SMTP catcher, so no real mail account is
   needed for local dev) from the repository root:

   ```bash
   docker compose up -d
   ```

   This starts `db` (Postgres, `127.0.0.1:5432`, user `postgres` / password `example`), `redis`
   (`127.0.0.1:6379`), and `smtp4dev` (web UI at http://localhost:5099, SMTP port `2525`).

   > **Windows note:** if you hit `SocketException: Cannot assign requested address` on startup,
   > make sure any connection strings/user-secrets use `127.0.0.1` rather than `localhost` — see
   > `agot-bg-website-dotnet/README.md` for why.

2. **Apply EF Core migrations** to create the `snr_dotnet` database and schema (requires the
   `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef`):

   ```bash
   cd agot-bg-website-dotnet
   dotnet ef database update --project agot-bg-website.Data --startup-project agot-bg-website
   ```

3. **Build the Tailwind CSS + DaisyUI theme** (`wwwroot/css/app.css` is a committed build
   artifact; you only need to rebuild it if you change the theme, but if you're not sure whether
   it's up to date, rebuilding is cheap and safe):

   ```bash
   cd agot-bg-website-dotnet/agot-bg-website/ClientAssets
   npm install
   npm run build      # one-off, minified
   # npm run watch    # or this instead, to rebuild on every change while iterating on styles
   ```

4. **(Optional) Configure local secrets** via `dotnet user-secrets` from
   `agot-bg-website-dotnet/agot-bg-website/` — OIDC client secrets (Google/Discord/Facebook),
   custom SMTP credentials, etc. Without any of this configured, the app still runs fully:
   external OIDC login buttons simply won't do anything (register/log in with username+password
   instead — see below), and outgoing email falls back to a logger that just logs what would have
   been sent instead of crashing. See `agot-bg-website-dotnet/README.md`'s "Running locally"
   section for the full list of secrets, including how to point email at smtp4dev instead of the
   fallback logger.

5. **Run the website**:

   ```bash
   cd agot-bg-website-dotnet/agot-bg-website
   dotnet run
   ```

   The website will be accessible at `http://localhost:8000/` (matching the old Django dev URL).
   Without the real game client built (see next section), `/play/<gameId>` serves a placeholder
   page so the rest of the site (registration, login, rooms, game list) can still be exercised
   end-to-end.

6. **Create your first user, and make it an admin.** There is no `createsuperuser` command/script
   — instead, register a normal account through the website's own sign-up form
   (`http://localhost:8000/Identity/Account/Register`, username/password), the same way any real
   user would. To grant that account the `Admin` role (which unlocks the `/Admin` area and the
   `ImpersonateOtherPlayers`/`CancelGame`/`ManageUserStatus` permissions — see
   `agot-bg-website-dotnet/agot-bg-website/Infrastructure/Auth/GamePermissions.cs`), connect to the
   `snr_dotnet` Postgres database with pgAdmin (or any other Postgres client) and run:

   ```sql
   INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
   SELECT u."Id", r."Id"
   FROM "AspNetUsers" u, "AspNetRoles" r
   WHERE u."UserName" = 'YourUsername' AND r."Name" = 'Admin';
   ```

   (Roles are seeded automatically on startup, so `AspNetRoles` will already contain `Member`,
   `Admin`, `High Member`, `Banned`, `On probation`, `Tongueless` rows — no need to insert one
   yourself.) If the user is already logged in, they'll need to log out and back in (or wait for
   the auth cookie's normal revalidation interval) for the new role/permissions to take effect.

**Note**: If you try to open a game via the website without having built the real game client
(next section), you will land on a placeholder template page.

### Launching the Game and the Website

To launch the 2 components and make them inter-connected, make sure the dependencies are installed and the database is up and running (follow the instructions given in the precedent sections).

Replace the environment configuration of the game-server with a live one: `cp .env.dev.live .env`.
This points the game server's private API client (`MASTER_API_BASE_URL`) at
`http://localhost:8001/api` — the website's internal, Basic-Auth-only port for the game server (as
opposed to port 8000, which serves the public site).

The front-end of the game server must be built and placed in the website. This can be done by
executing, from the repository root:

```powershell
.\build_and_place_game_client_into_dotnet.ps1
```

(or `./build_and_place_game_client_into_dotnet.sh` on Linux/macOS). This builds
`agot-bg-game-server`'s client and copies the resulting static assets/`index.html` into
`agot-bg-website-dotnet/agot-bg-website/wwwroot/static_game/` and
`agot-bg-website-dotnet/agot-bg-website/GameClientTemplates/play.html` respectively. Restart
`dotnet run` afterwards to pick up the newly-placed template.

You can now run the game server and the website by launching, in 2 different terminals:

* In `agot-bg-game-server/`, execute `yarn run run-server`.
* In `agot-bg-website-dotnet/agot-bg-website/`, execute `dotnet run`.

**Note**: For play testing you at least need another user to run the game variant "Teach the game" locally. Create this user the same way you created your first user (register through the sign-up form).

## More details

See `agot-bg-website-dotnet/README.md` for the full picture: solution structure, every local
secret the app understands (email providers, OIDC apps), the Tailwind/DaisyUI build in more
depth, the game-client integration script, and how to run the legacy-data importer
(`Snr.Migration`, for migrating an existing production database into a fresh one). See
`agot-bg-website-dotnet/MIGRATION_PLAN.md` for the underlying design decisions and Django→ASP.NET
Core mapping.
