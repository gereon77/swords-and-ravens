import {Server} from "ws";
import GlobalServer from "./GlobalServer";
import * as dotenv from "dotenv";
import * as Sentry from "@sentry/node";
dotenv.config();

// Setup Sentry
if (process.env.SENTRY_DSN) {
    const SENTRY_DSN = process.env.SENTRY_DSN;
    Sentry.init({dsn: SENTRY_DSN});
}

const wsServer = new Server({port: parseInt(process.env.PORT || "5000")});

const globalServer = new GlobalServer(wsServer);
globalServer.start();
globalServer.runBackgroundTasks();

// Ensure any save still sitting inside EntireGame's throttle window gets flushed and actually
// sent (and awaited) before the process exits - otherwise a deploy/restart landing inside that
// window silently drops the most recent save, and the website is left with a stale snapshot of a
// live game. See GlobalServer.shutdown()'s doc comment.
let shuttingDown = false;
async function shutdown(signal: string): Promise<void> {
    if (shuttingDown) {
        return;
    }
    shuttingDown = true;

    console.log(`Received ${signal}, flushing pending game saves before exit...`);
    try {
        await globalServer.shutdown();
    } catch (e) {
        Sentry.captureException(e);
    }
    process.exit(0);
}

process.on("SIGTERM", () => void shutdown("SIGTERM"));
process.on("SIGINT", () => void shutdown("SIGINT"));