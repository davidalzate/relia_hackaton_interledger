import { createHash, timingSafeEqual } from 'node:crypto';
import Fastify, { type FastifyError, type FastifyInstance } from 'fastify';
import rateLimit from '@fastify/rate-limit';
import { PROBLEM_CONTENT_TYPE, ProblemError } from './problem.js';
import { PAYMENT_POINTER_PATTERN, type ResolvedWallet, type WalletResolver } from './wallets.js';

export type ReliaGrantStatus = 'active' | 'awaiting_approval' | 'exhausted' | 'missing';

export interface AppDeps {
  apiKey: string;
  logLevel: string;
  healthRateLimitPerMinute: number;
  reliaWallet: ResolvedWallet;
  reliaGrantStatus: () => ReliaGrantStatus;
  wallets: Pick<WalletResolver, 'resolve'>;
}

declare module 'fastify' {
  interface FastifyContextConfig {
    /** Ruta sin `X-Api-Key`. */
    public?: boolean;
  }
}

export async function buildApp(deps: AppDeps): Promise<FastifyInstance> {
  const app = Fastify({
    logger: { level: deps.logLevel, redact: ['req.headers["x-api-key"]'] },
    // El contrato valida con sus propios patrones; no coaccionar tipos en silencio.
    ajv: { customOptions: { coerceTypes: false, removeAdditional: false } },
  });

  app.setErrorHandler((error: FastifyError | ProblemError, request, reply) => {
    let problem: ProblemError;
    if (error instanceof ProblemError) {
      problem = error;
    } else if (error.validation || error.code === 'FST_ERR_CTP_INVALID_JSON_BODY' || error.statusCode === 400) {
      problem = new ProblemError(422, 'validation_error', 'Solicitud inválida', error.message);
    } else if (error.statusCode === 415) {
      problem = new ProblemError(422, 'validation_error', 'Solicitud inválida', error.message);
    } else {
      request.log.error({ err: error }, 'error no controlado');
      problem = new ProblemError(500, 'internal_error', 'Error interno');
    }
    reply.status(problem.status).type(PROBLEM_CONTENT_TYPE).send(problem.toProblem());
  });

  app.setNotFoundHandler((request, reply) => {
    const problem = new ProblemError(404, 'not_found', 'Recurso inexistente', `${request.method} ${request.url}`);
    reply.status(404).type(PROBLEM_CONTENT_TYPE).send(problem.toProblem());
  });

  await app.register(rateLimit, {
    global: false,
    errorResponseBuilder: (_request, context) =>
      new ProblemError(429, 'too_many_requests', 'Demasiadas consultas', `Intenta de nuevo en ${context.after}.`),
  });

  const expectedKey = digest(deps.apiKey);
  app.addHook('onRequest', async (request) => {
    if (request.routeOptions.config?.public) return;
    const provided = request.headers['x-api-key'];
    if (typeof provided !== 'string' || !timingSafeEqual(digest(provided), expectedKey)) {
      throw new ProblemError(401, 'unauthorized', 'No autorizado', 'Falta X-Api-Key o es incorrecta.');
    }
  });

  app.get(
    '/health',
    { config: { public: true, rateLimit: { max: deps.healthRateLimitPerMinute, timeWindow: '1 minute' } } },
    async () => {
      const reliaGrant = deps.reliaGrantStatus();
      const { pointer, url, assetCode, assetScale } = deps.reliaWallet;
      return {
        status: reliaGrant === 'active' ? 'ok' : 'degraded',
        rail: 'openPayments',
        reliaWallet: { pointer, url, assetCode, assetScale },
        reliaGrant,
      };
    },
  );

  app.get<{ Querystring: { pointer: string } }>(
    '/wallet-addresses',
    {
      schema: {
        querystring: {
          type: 'object',
          required: ['pointer'],
          properties: { pointer: { type: 'string', pattern: PAYMENT_POINTER_PATTERN.source } },
        },
      },
    },
    async (request) => {
      const { pointer, url, assetCode, assetScale } = await deps.wallets.resolve(request.query.pointer);
      return { pointer, url, assetCode, assetScale };
    },
  );

  return app;
}

// Comparar huellas de igual largo permite timingSafeEqual sin filtrar la longitud de la clave.
function digest(value: string): Buffer {
  return createHash('sha256').update(value).digest();
}
