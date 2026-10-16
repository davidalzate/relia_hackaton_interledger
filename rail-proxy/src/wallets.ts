import { createPublicKey, type KeyObject } from 'node:crypto';
import { OpenPaymentsClientError, type AuthenticatedClient, type WalletAddress } from '@interledger/open-payments';
import { ProblemError } from './problem.js';

export const PAYMENT_POINTER_PATTERN = /^\$[^\s/]+(\/\S*)?$/;

/** Billetera resuelta en el riel, con la forma de `WalletAddress` del contrato más lo que el proxy necesita. */
export interface ResolvedWallet {
  pointer: string;
  /** URL canónica (`walletAddress.id`). Se compara por aquí, nunca por el texto del pointer. */
  url: string;
  assetCode: string;
  assetScale: number;
  authServer: string;
  resourceServer: string;
}

/** `$ilp.interledger-test.dev/rosa` → `https://ilp.interledger-test.dev/rosa`; sin ruta → `/.well-known/pay`. */
export function pointerToUrl(pointer: string): string {
  const rest = pointer.slice(1);
  const slash = rest.indexOf('/');
  const host = slash === -1 ? rest : rest.slice(0, slash);
  const path = slash === -1 ? '' : rest.slice(slash);
  return `https://${host}${path === '' || path === '/' ? '/.well-known/pay' : path}`;
}

export function urlToPointer(url: string): string {
  const { host, pathname } = new URL(url);
  return pathname === '/.well-known/pay' ? `$${host}` : `$${host}${pathname.replace(/\/$/, '')}`;
}

/** Acepta un payment pointer o una URL https. */
export function toWalletUrl(pointerOrUrl: string): string {
  if (PAYMENT_POINTER_PATTERN.test(pointerOrUrl)) return pointerToUrl(pointerOrUrl);
  const url = new URL(pointerOrUrl);
  if (url.protocol !== 'https:') throw new Error(`La billetera debe ser un payment pointer o una URL https: ${pointerOrUrl}`);
  return url.toString().replace(/\/$/, '');
}

export async function resolveWallet(client: AuthenticatedClient, pointerOrUrl: string): Promise<ResolvedWallet> {
  let wallet: WalletAddress;
  try {
    wallet = await client.walletAddress.get({ url: toWalletUrl(pointerOrUrl) });
  } catch (error) {
    throw classifyWalletError(error);
  }
  return {
    pointer: urlToPointer(wallet.id),
    url: wallet.id,
    assetCode: wallet.assetCode,
    assetScale: wallet.assetScale,
    authServer: wallet.authServer,
    resourceServer: wallet.resourceServer,
  };
}

function classifyWalletError(error: unknown): ProblemError {
  const detail =
    error instanceof OpenPaymentsClientError
      ? `${error.description}${error.status ? ` (HTTP ${error.status})` : ''}`
      : error instanceof Error
        ? error.message
        : String(error);
  if (error instanceof OpenPaymentsClientError && error.status === 404) {
    return new ProblemError(404, 'wallet_not_found', 'Billetera inexistente', detail);
  }
  return new ProblemError(503, 'rail_unavailable', 'El riel no respondió', detail);
}

/** Confirma que la billetera publica la llave pública que corresponde a la llave privada configurada. */
export async function assertKeyPublished(
  client: AuthenticatedClient,
  wallet: ResolvedWallet,
  keyId: string,
  privateKey: KeyObject,
): Promise<void> {
  const jwks = await client.walletAddress.getKeys({ url: wallet.url });
  const published = jwks.keys.find((key) => key.kid === keyId);
  if (!published) {
    throw new Error(`La billetera ${wallet.pointer} no publica la llave ${keyId}. Revisa RELIA_KEY_ID.`);
  }
  const ours = createPublicKey(privateKey).export({ format: 'jwk' });
  if (ours.x !== published.x) {
    throw new Error(`La llave privada no corresponde a la llave ${keyId} publicada en ${wallet.pointer}.`);
  }
}
