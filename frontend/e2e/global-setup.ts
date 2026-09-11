import { createConnection } from 'node:net';

const databaseHost = 'localhost';
const databasePort = 1433;

function isListening(host: string, port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = createConnection({ host, port });
    const settle = (reachable: boolean) => {
      socket.destroy();
      resolve(reachable);
    };

    socket.setTimeout(2_000);
    socket.once('connect', () => settle(true));
    socket.once('timeout', () => settle(false));
    socket.once('error', () => settle(false));
  });
}

/**
 * The API does not start without the database, and the failure otherwise shows
 * up as a three minute wait for a web server that was never going to answer.
 * Fail in two seconds with the command that fixes it instead.
 */
export default async function globalSetup(): Promise<void> {
  if (await isListening(databaseHost, databasePort)) return;

  throw new Error(
    `No SQL Server listening on ${databaseHost}:${databasePort}. Start it with "docker compose up -d" from the repository root.`,
  );
}
