import { createAuthenticatedClient } from '@interledger/open-payments';
import { buildApp } from './app.js';
import { loadConfig } from './config.js';
import { openStore } from './store.js';
import { assertKeyPublished, resolveWallet, toWalletUrl } from './wallets.js';

async function main(): Promise<void> {
  const config = loadConfig();

  const client = await createAuthenticatedClient({
    walletAddressUrl: toWalletUrl(config.reliaWalletAddress),
    privateKey: config.reliaPrivateKey,
    keyId: config.reliaKeyId,
    logLevel: 'warn',
  });

  const reliaWallet = await resolveWallet(client, config.reliaWalletAddress).catch((error: Error) => {
    throw new Error(`No se pudo resolver la billetera de Relia ${config.reliaWalletAddress}: ${error.message}`);
  });
  await assertKeyPublished(client, reliaWallet, config.reliaKeyId, config.reliaPrivateKey);

  const db = openStore(config.databasePath);

  const app = await buildApp({
    apiKey: config.apiKey,
    logLevel: config.logLevel,
    healthRateLimitPerMinute: config.healthRateLimitPerMinute,
    reliaWallet,
    reliaGrantStatus: () => 'missing',
  });

  const shutdown = async (signal: string) => {
    app.log.info({ signal }, 'apagando');
    await app.close();
    db.close();
    process.exit(0);
  };
  process.once('SIGINT', shutdown);
  process.once('SIGTERM', shutdown);

  await app.listen({ host: config.host, port: config.port });
  app.log.info({ reliaWallet: reliaWallet.pointer, assetCode: reliaWallet.assetCode }, 'proxy de riel listo');
}

main().catch((error: unknown) => {
  console.error(`[rail-proxy] No se pudo arrancar: ${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
});
