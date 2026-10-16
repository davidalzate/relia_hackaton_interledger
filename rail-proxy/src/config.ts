import { createPrivateKey, type KeyObject } from 'node:crypto';

export interface Config {
  host: string;
  port: number;
  logLevel: string;
  apiKey: string;
  /** Payment pointer ($host/ruta) o URL https de la billetera de Relia. */
  reliaWalletAddress: string;
  reliaKeyId: string;
  reliaPrivateKey: KeyObject;
  databasePath: string;
  healthRateLimitPerMinute: number;
}

export class ConfigError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ConfigError';
  }
}

const REQUIRED = ['RAIL_PROXY_API_KEY', 'RELIA_WALLET_ADDRESS', 'RELIA_KEY_ID', 'RELIA_PRIVATE_KEY_BASE64'] as const;

export function loadConfig(env: NodeJS.ProcessEnv = process.env): Config {
  const missing = REQUIRED.filter((name) => !env[name]?.trim());
  if (missing.length > 0) {
    throw new ConfigError(`Faltan variables de entorno: ${missing.join(', ')}.`);
  }

  return {
    host: env.HOST?.trim() || '0.0.0.0',
    port: parsePositiveInt(env, 'PORT', 4000),
    logLevel: env.LOG_LEVEL?.trim() || 'info',
    apiKey: env.RAIL_PROXY_API_KEY!.trim(),
    reliaWalletAddress: env.RELIA_WALLET_ADDRESS!.trim(),
    reliaKeyId: env.RELIA_KEY_ID!.trim(),
    reliaPrivateKey: decodePrivateKey(env.RELIA_PRIVATE_KEY_BASE64!.trim()),
    databasePath: env.DATABASE_PATH?.trim() || 'data/rail-proxy.db',
    healthRateLimitPerMinute: parsePositiveInt(env, 'HEALTH_RATE_LIMIT_PER_MINUTE', 60),
  };
}

function parsePositiveInt(env: NodeJS.ProcessEnv, name: string, fallback: number): number {
  const raw = env[name]?.trim();
  if (!raw) return fallback;
  const value = Number(raw);
  if (!Number.isInteger(value) || value <= 0) {
    throw new ConfigError(`${name} debe ser un entero positivo (recibido: "${raw}").`);
  }
  return value;
}

// La llave es el private.key (PEM) que descarga la wallet de prueba, codificado en base64.
function decodePrivateKey(base64: string): KeyObject {
  const pem = Buffer.from(base64, 'base64').toString('utf8');
  if (!pem.includes('-----BEGIN')) {
    throw new ConfigError('RELIA_PRIVATE_KEY_BASE64 no contiene una llave PEM codificada en base64.');
  }
  let key: KeyObject;
  try {
    key = createPrivateKey(pem);
  } catch {
    throw new ConfigError('RELIA_PRIVATE_KEY_BASE64 no es una llave privada válida.');
  }
  if (key.asymmetricKeyType !== 'ed25519') {
    throw new ConfigError(`RELIA_PRIVATE_KEY_BASE64 debe ser Ed25519 (es ${key.asymmetricKeyType}).`);
  }
  return key;
}
