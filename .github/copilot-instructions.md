# Swords and Ravens repository instructions

## Repository architecture

- The repository has two cooperating applications:
  - `agot-bg-website/` is a Django 3 application. `agotboardgame_main` owns users, game records, pages, and the generated game-host template; `api` is the private REST boundary used by the game server; `chat` is a Django Channels WebSocket application backed by Redis.
  - `agot-bg-game-server/` is a TypeScript application containing both the authoritative WebSocket game server and the React/MobX browser client. `src/common` is shared game logic, `src/server` owns connections and persistence integration, `src/client` owns UI and the mirrored client state, and `src/messages` defines the wire protocol.
- Django is the control and persistence plane, but the TypeScript server is authoritative while a game is running. `GlobalServer` loads and saves `Game.serialized_game`, the lightweight `view_of_game`, player metadata, state, and serialization version through `WebsiteClient`; it also calls Django for notifications and chat-room operations.
- Game flow is a nested state machine rooted at `EntireGame`. Each `GameState` has a parent and optional child; the current phase is the leaf. The server processes a client action, mutates this tree, and sends either typed incremental `ServerMessage`s or a serialized changed subtree. The browser maintains the corresponding tree and MobX observables render it.
- Production builds compile the React client into `dist/`, copy the assets to Django's `static_game/`, and use the generated `index.html` as `agotboardgame_main/templates/agotboardgame_main/play.html`. Django injects authentication JSON into that template. `build_and_place_game_client_into_django.sh` performs the same integration for local development; `website.Dockerfile` performs it for deployment.

## Build, run, lint, and test

Run TypeScript commands from `agot-bg-game-server/` (Node.js 16 and Yarn):

```bash
yarn install --frozen-lockfile
yarn run generate-json-schemas
yarn run run-server                 # ts-node WebSocket server
yarn run run-client                 # webpack dev server
yarn run build-client               # production browser bundle
yarn run build-local-client         # bundle for local Django integration
yarn run lint
yarn tsc --noEmit                   # type-check
yarn jest                           # all Jest tests
yarn jest tests/path/example.test.ts
yarn jest tests/path/example.test.ts -t "test name"
```

Jest only discovers `agot-bg-game-server/tests/**/*test.ts`; no TypeScript tests are currently tracked. The Django `tests.py` modules are also currently placeholders.

Run Django commands from `agot-bg-website/` with its virtual environment active:

```bash
pip install -r requirements.txt
python manage.py migrate
python manage.py test
python manage.py test api.tests
python manage.py test app.tests.TestClass.test_method
python manage.py runserver
```

PostgreSQL and Redis for Django are provided by `docker-compose up` at the repository root. The full local database bootstrap has special migration-copy steps; follow the root `README.md` rather than inventing a fresh migration sequence. CI validates both deployable images with:

```bash
docker build . -f game_server.Dockerfile
docker build . -f website.Dockerfile
```

Before committing any C# change in `agot-bg-website-dotnet/`, run `dotnet csharpier format .` from that directory (the local dotnet tool declared in `agot-bg-website-dotnet/.config/dotnet-tools.json`) to keep formatting consistent.

### Verifying changes to `agot-bg-website-dotnet/`

- **Usual verify** (default after any change): `dotnet build agot-bg-website -c Release` +
  `dotnet test agot-bg-website.Tests -c Release`. This is enough for routine changes.
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

## Django and integration conventions

- `agotboardgame_main.models.Game.serialized_game` is the complete resumable state; `view_of_game` is the smaller denormalized summary used by website lists and the public endpoint. Changes to game status or player summaries may require updating both TypeScript serialization and `api.serializers.GameSerializer`.
- The game server's Django contract is represented on both sides by `src/server/website-client/WebsiteClient.ts`/`LiveWebsiteClient.ts` and `agot-bg-website/api/`. Change the interface, implementation, URL/view, serializer, and permissions together.
- Django uses the custom UUID-based `agotboardgame_main.User` model and `game_token` for game-server authentication. The React production entry reads the Django-injected `auth-data`; local webpack development instead derives synthetic credentials from the URL hash.
- Chat does not pass through the game-server WebSocket. The React `ChatClient` connects directly to Django Channels routes, while the game server asks Django's API to create or clear rooms.
- Django database migrations and TypeScript serialized-game migrations solve different compatibility problems. Model changes need normal Django migrations; changes to the JSON game-state shape may additionally need a serialized-game migration.
- The generated `play.html` and files under `static_game/` are build outputs. Change `agot-bg-game-server/public/index.html` or the webpack/client source and rebuild instead of hand-editing the generated Django template or bundle.
