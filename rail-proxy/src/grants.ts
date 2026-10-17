import { isFinalizedGrantWithAccessToken, OpenPaymentsClientError, type AuthenticatedClient } from '@interledger/open-payments';

type NonInteractiveType = 'incoming-payment' | 'quote';

const ACCESS = {
  'incoming-payment': { type: 'incoming-payment', actions: ['create', 'read', 'complete'] },
  quote: { type: 'quote', actions: ['create', 'read'] },
} as const;

// Renovar un poco antes de que el riel deje de aceptar el token.
const EXPIRY_MARGIN_MS = 30_000;

/**
 * Tokens de grants no interactivos (`incoming-payment`, `quote`), en memoria por servidor de autorización.
 * Son baratos de volver a pedir, así que no se persisten: tras un reinicio se piden de nuevo.
 */
export class NonInteractiveGrants {
  private readonly cache = new Map<string, { value: string; expiresAt: number }>();
  private readonly inflight = new Map<string, Promise<string>>();

  constructor(
    private readonly client: AuthenticatedClient,
    private readonly now: () => number = Date.now,
  ) {}

  /** Ejecuta `use` con un token vigente; si el riel lo rechaza con 401, pide otro y reintenta una vez. */
  async withToken<T>(authServer: string, type: NonInteractiveType, use: (accessToken: string) => Promise<T>): Promise<T> {
    const token = await this.token(authServer, type);
    try {
      return await use(token);
    } catch (error) {
      if (!(error instanceof OpenPaymentsClientError && error.status === 401)) throw error;
      this.cache.delete(key(authServer, type));
      return use(await this.token(authServer, type));
    }
  }

  private async token(authServer: string, type: NonInteractiveType): Promise<string> {
    const k = key(authServer, type);
    const cached = this.cache.get(k);
    if (cached && cached.expiresAt > this.now()) return cached.value;

    // Una sola petición en vuelo por clave.
    let pending = this.inflight.get(k);
    if (!pending) {
      pending = this.request(authServer, type).finally(() => this.inflight.delete(k));
      this.inflight.set(k, pending);
    }
    return pending;
  }

  private async request(authServer: string, type: NonInteractiveType): Promise<string> {
    const grant = await this.client.grant.request(
      { url: authServer },
      {
        access_token: {
          access: [
            type === 'quote'
              ? { type: 'quote', actions: [...ACCESS.quote.actions] }
              : { type: 'incoming-payment', actions: [...ACCESS['incoming-payment'].actions] },
          ],
        },
      },
    );
    if (!isFinalizedGrantWithAccessToken(grant)) {
      throw new OpenPaymentsClientError('grant no interactivo pendiente', {
        description: `El servidor de autorización pidió interacción para ${type}`,
      });
    }
    const expiresIn = grant.access_token.expires_in;
    this.cache.set(key(authServer, type), {
      value: grant.access_token.value,
      expiresAt: expiresIn !== undefined ? this.now() + expiresIn * 1000 - EXPIRY_MARGIN_MS : Number.POSITIVE_INFINITY,
    });
    return grant.access_token.value;
  }
}

function key(authServer: string, type: NonInteractiveType): string {
  return `${authServer}|${type}`;
}
