import { createAuthenticatedClient } from '@interledger/open-payments';
import { pino } from 'pino';
import { buildApp } from './app.js';
import { loadConfig } from './config.js';
import { NonInteractiveGrants } from './grants.js';
import { PaymentService } from './payments.js';
import { ReliaGrantService } from './relia-grant.js';
import { openStore } from './store.js';
import { Synchronizer } from './synchronizer.js';
import { assertKeyPublished, toWalletUrl, WalletResolver } from './wallets.js';

async function main(): Promise<void> {
  const config = loadConfig();
  const logger = pino({ level: config.logLevel, redact: ['req.headers["x-api-key"]'] });

  const client = await createAuthenticatedClient({
    walletAddressUrl: toWalletUrl(config.reliaWalletAddress),
    privateKey: config.reliaPrivateKey,
    keyId: config.reliaKeyId,
    // El SDK registra la URL de cada petición fallida (p. ej. la continuación GNAP). Los errores del riel
    // los registra el proxy con describeRailError.
    logLevel: 'silent',
    // Una petición colgada no debe frenar al sincronizador ni a un POST.
    requestTimeoutMs: 10_000,
  });

  const wallets = new WalletResolver(client);
  const reliaWallet = await wallets.resolve(config.reliaWalletAddress).catch((error: Error) => {
    throw new Error(`No se pudo resolver la billetera de Relia ${config.reliaWalletAddress}: ${error.message}`);
  });
  await assertKeyPublished(client, reliaWallet, config.reliaKeyId, config.reliaPrivateKey);

  const db = openStore(config.databasePath);
  const reliaGrants = new ReliaGrantService(db, client, reliaWallet, logger.child({ module: 'relia-grant' }));
  const payments = new PaymentService({
    db,
    client,
    wallets,
    reliaWallet,
    reliaGrants,
    grants: new NonInteractiveGrants(client),
    log: logger.child({ module: 'payments' }),
  });
  const synchronizer = new Synchronizer(
    [
      { name: 'relia-grant', run: () => reliaGrants.sync() },
      { name: 'payments', run: () => payments.sync() },
    ],
    logger.child({ module: 'synchronizer' }),
  );

  const app = await buildApp({
    apiKey: config.apiKey,
    logger,
    healthRateLimitPerMinute: config.healthRateLimitPerMinute,
    reliaWallet,
    reliaGrants,
    wallets,
    payments,
  });

  const shutdown = async (signal: string) => {
    logger.info({ signal }, 'apagando');
    await app.close();
    await synchronizer.stop();
    await reliaGrants.drain();
    db.close();
    process.exit(0);
  };
  process.once('SIGINT', shutdown);
  process.once('SIGTERM', shutdown);

  await app.listen({ host: config.host, port: config.port });
  synchronizer.start();
  logger.info(
    { reliaWallet: reliaWallet.pointer, assetCode: reliaWallet.assetCode, reliaGrant: reliaGrants.status() },
    'proxy de riel listo',
  );
}

main().catch((error: unknown) => {
  console.error(`[rail-proxy] No se pudo arrancar: ${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
});
