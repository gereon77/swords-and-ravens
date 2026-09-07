# Swords and Ravens repository instructions

## Repository architecture

- The repository has two cooperating applications:
  - `agot-bg-website-dotnet/` is an ASP.NET Core application (Razor Pages + Minimal API), the sole
    website — it fully replaces the old Django `agot-bg-website`, which has been retired
    (only inert config/env leftovers remain in `agot-bg-website/`, no app code). The `agot-bg-website`
    project inside it owns Identity/auth (username+password plus Google/Discord/Facebook OIDC),
    game records, rooms, and the generated game-host template; `Api/` (`GamesApi`, `RoomsApi`,
    `UsersApi`, `PublicApi`, `NotificationsApi`, `PlayApi`, `ChatWebSocketApi`) is the private REST
    boundary used by the game server; chat is a hand-rolled ASP.NET Core WebSocket endpoint backed
    by Redis pub/sub for fan-out. `agot-bg-website.Data` holds the EF Core `DbContext`/entities/
    migrations; `Snr.Migration` is the one-off console tool that imports the legacy Django
    database into this schema. See `agot-bg-website-dotnet/README.md` and `MIGRATION_PLAN.md` for
    full detail.
  - `agot-bg-game-server/` is a TypeScript application containing both the authoritative WebSocket game server and the React/MobX browser client. `src/common` is shared game logic, `src/server` owns connections and persistence integration, `src/client` owns UI and the mirrored client state, and `src/messages` defines the wire protocol.
- The website is the control and persistence plane, but the TypeScript server is authoritative while a game is running. `GlobalServer` loads and saves `Game.SerializedGame`, the lightweight `ViewOfGame`, player metadata, state, and serialization version through `WebsiteClient`/`LiveWebsiteClient`; it also calls the website's API for notifications and chat-room operations.
- Game flow is a nested state machine rooted at `EntireGame`. Each `GameState` has a parent and optional child; the current phase is the leaf. The server processes a client action, mutates this tree, and sends either typed incremental `ServerMessage`s or a serialized changed subtree. The browser maintains the corresponding tree and MobX observables render it.
- Production builds compile the React client into `dist/`, copy the assets to the website's `wwwroot/static_game/`, and use the generated `index.html` as `GameClientTemplates/play.html`. `build_and_place_game_client_into_dotnet.ps1`/`.sh` performs the same integration for local development; the website's own `Dockerfile` performs it for deployment.

## Build, run, lint, and test

Run TypeScript commands from `agot-bg-game-server/` (Node.js LTS, currently 24.x, and Yarn):

```bash
yarn install --frozen-lockfile
yarn run generate-json-schemas
yarn run run-server                 # ts-node WebSocket server
yarn run run-client                 # webpack dev server
yarn run build-client               # production browser bundle
yarn run build-local-client         # bundle for local website integration
yarn run lint
yarn tsc --noEmit                   # type-check
yarn jest                           # all Jest tests
yarn jest tests/path/example.test.ts
yarn jest tests/path/example.test.ts -t "test name"
```

Jest only discovers `agot-bg-game-server/tests/**/*test.ts`; no TypeScript tests are currently tracked.

Run .NET commands from `agot-bg-website-dotnet/` (see that folder's `README.md` for the full
local-dev setup: Postgres/Redis/smtp4dev via `docker compose up -d` at the repo root, EF Core
migrations, Tailwind/DaisyUI build, user-secrets):

```bash
dotnet build
dotnet test
dotnet ef database update --project agot-bg-website.Data --startup-project agot-bg-website
dotnet run --project agot-bg-website
```

CI validates the deployable images with:

```bash
docker build . -f game_server.Dockerfile
docker build -f agot-bg-website-dotnet/agot-bg-website/Dockerfile agot-bg-website-dotnet
```

Before committing any C# change in `agot-bg-website-dotnet/`, run `dotnet csharpier format .` from that directory (the local dotnet tool declared in `agot-bg-website-dotnet/.config/dotnet-tools.json`) to keep formatting consistent.

### Verifying changes to `agot-bg-website-dotnet/`

- **Usual verify** (default after any change): `dotnet build agot-bg-website -c Release` +
  `dotnet test agot-bg-website.Tests -c Release`. This is enough for routine changes.
- **Markup/CSS-only changes** (a `.cshtml` change limited to class attributes/HTML structure, or a
  `ClientAssets`/`wwwroot` CSS change, with no C# code touched): skip `dotnet test` — there is no
  test coverage for markup/CSS, so it's a wasted step. Still run `dotnet build` and
  `dotnet csharpier format .`.
- **Extended verify** (only when explicitly requested): additionally run the app (`dotnet run` or
  a Docker container) and `curl` against it to confirm real HTTP behavior end to end.
- Only start a running process (`dotnet run`, `docker run`, etc.) and curl against it when the
  user explicitly asks for "verify" or "extended verify" of a running instance — never spin one up
  on your own initiative just to double-check a change, since it risks colliding with an instance
  the user already has running locally (port conflicts, stale listeners) and leaves stray
  processes/containers behind if not cleaned up.

## Game-server conventions

- Client and server messages are discriminated unions in `src/messages/ClientMessage.ts` and `ServerMessage.ts`. When changing a client message, update every sender and handler and run `yarn run generate-json-schemas`; the generated `src/server/ClientMessage.json` is the AJV validation schema used before dispatch.
- Advance game phases with `setChildGameState(...).firstStart()` and delegate messages to the active child, following nearby state classes. Do not assign a new child directly on the server: `setChildGameState` marks the subtree for transmission and changes `leafStateId`.
- A new or changed game state normally requires all of these surfaces to remain aligned: its serialized interface and stable string `type`, `serializeToClient`, `deserializeFromServer`, the parent's `deserializeChildGameState` switch, the parent's child-state union, and the matching React component entry passed to `renderChildGameState`.
- Serialization is audience-aware. `serializeToClient(admin, player)` may hide cards, bids, objectives, or other private state. Preserve that filtering when adding fields; do not expose the server's object graph directly.
- Wire and serialized data use stable IDs and arrays of tuples rather than class instances or native maps. Reconstruct references through the owning game/world and use the repository's `BetterMap` where surrounding code does.
- Persisted games survive deployments. If a change makes an older `serialized_game` incompatible, append the next numeric migration to `src/server/serializedGameMigrations.ts`; never rewrite old migrations. `GlobalServer.latestSerializedGameVersion` is derived from the final entry.
- Static board/setup/card data is concentrated in `data/baseGameData.json` and registries under `src/common/ingame-game-state/game-data-structure/`. Keep data IDs synchronized with ability/type registries and asset lookup tables instead of duplicating variant rules in UI components.
- Shared `src/common` classes run on both server and browser. Keep server-only I/O in `src/server`, browser APIs and presentation in `src/client`, and communicate through callbacks/messages already exposed by `EntireGame`.
- Prefer micro-optimizations that avoid unnecessary role checks, such as checking admin role only inside the branch where it is needed.

## Website (`agot-bg-website-dotnet`) and integration conventions

- `Game.SerializedGame` (EF Core entity in `agot-bg-website.Data/Domain/GameEntities.cs`) is the complete resumable state; `Game.ViewOfGame` is the smaller denormalized summary used by website lists and the public endpoint. Both are opaque `JsonDocument` blobs owned by the TS game server. Changes to game status or player summaries may require updating both TypeScript serialization and the corresponding DTOs in `Api/GamesApi.cs`.
- The game server's website contract is represented on both sides by `src/server/website-client/WebsiteClient.ts`/`LiveWebsiteClient.ts` and `agot-bg-website-dotnet/agot-bg-website/Api/` (`GamesApi`, `RoomsApi`, `UsersApi`, `NotificationsApi`). Change the interface, implementation, Minimal API route, DTO, and authorization policy together.
- The website uses a custom Guid-keyed `ApplicationUser` (`agot-bg-website.Data/Domain/ApplicationUser.cs`, built on ASP.NET Core Identity) and a per-user `GameToken` for game-server authentication. `PlayApi` injects an `auth-data` JSON blob (containing `authToken = user.GameToken`, etc.) into the served `play.html`, mirroring how Django injected its own `auth-data`; local webpack development instead derives synthetic credentials from the URL hash.
- Chat does not pass through the game-server WebSocket. The React `ChatClient` connects directly to the website's own `ChatWebSocketApi` (a hand-rolled ASP.NET Core WebSocket endpoint backed by Redis pub/sub for fan-out), while the game server asks the website's API to create or clear rooms.
- EF Core migrations (`agot-bg-website.Data/Migrations`) and TypeScript serialized-game migrations solve different compatibility problems. Entity/schema changes need a normal EF Core migration (`dotnet ef migrations add ...`); changes to the JSON game-state shape may additionally need a serialized-game migration in `agot-bg-game-server/src/server/serializedGameMigrations.ts`.
- The generated `GameClientTemplates/play.html` and files under `wwwroot/static_game/` are build outputs. Change `agot-bg-game-server/public/index.html` or the webpack/client source and rebuild (`build_and_place_game_client_into_dotnet.ps1`/`.sh`) instead of hand-editing the generated template or bundle.
