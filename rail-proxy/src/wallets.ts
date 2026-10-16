import { createPublicKey, type KeyObject } from 'node:crypto';
import { lookup } from 'node:dns/promises';
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

const DEFAULT_CACHE_TTL_MS = 5 * 60 * 1000;

/**
 * Resuelve payment pointers en el riel y guarda en caché las respuestas exitosas unos minutos.
 * Lanza `ProblemError` con `wallet_not_found` (404) o `rail_unavailable` (503).
 */
export class WalletResolver {
  private readonly cache = new Map<string, { wallet: ResolvedWallet; expiresAt: number }>();

  constructor(
    private readonly client: AuthenticatedClient,
    private readonly cacheTtlMs = DEFAULT_CACHE_TTL_MS,
  ) {}

  async resolve(pointerOrUrl: string): Promise<ResolvedWallet> {
    const url = toWalletUrl(pointerOrUrl);
    const cached = this.cache.get(url);
    if (cached && cached.expiresAt > Date.now()) return cached.wallet;

    let wallet: WalletAddress;
    try {
      wallet = await this.client.walletAddress.get({ url });
    } catch (error) {
      throw await classifyWalletError(url, error);
    }
    const resolved: ResolvedWallet = {
      pointer: urlToPointer(wallet.id),
      url: wallet.id,
      assetCode: wallet.assetCode,
      assetScale: wallet.assetScale,
      authServer: wallet.authServer,
      resourceServer: wallet.resourceServer,
    };
    this.cache.set(url, { wallet: resolved, expiresAt: Date.now() + this.cacheTtlMs });
    return resolved;
  }
}

// El SDK no expone la causa de un fallo de red: "fetch failed" sale igual para un dominio
// inexistente que para un riel caído. Se distingue con una consulta DNS.
async function classifyWalletError(url: string, error: unknown): Promise<ProblemError> {
  const notFound = (detail: string) => new ProblemError(404, 'wallet_not_found', 'Billetera inexistente', detail);
  const unavailable = (detail: string) => new ProblemError(503, 'rail_unavailable', 'El riel no respondió', detail);

  if (!(error instanceof OpenPaymentsClientError)) {
    return unavailable(error instanceof Error ? error.message : String(error));
  }
  // La descripción puede traer el cuerpo entero de una página HTML.
  const description = error.description.length > 200 ? `${error.description.slice(0, 200)}…` : error.description;
  const detail = `${description}${error.status ? ` (HTTP ${error.status})` : ''}`;
  if (error.status !== undefined) {
    return error.status >= 500 || error.status === 429 ? unavailable(detail) : notFound(detail);
  }
  if (error.description === 'fetch failed' || /timeout|abort/i.test(error.description)) {
    return (await hostExists(new URL(url).hostname)) ? unavailable(detail) : notFound(`${detail}: el dominio no existe`);
  }
  // Respondió algo que no es una wallet address (HTML, JSON con otra forma).
  return notFound(detail);
}

async function hostExists(hostname: string): Promise<boolean> {
  try {
    await lookup(hostname);
    return true;
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code;
    return code !== 'ENOTFOUND' && code !== 'ENODATA';
  }
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
