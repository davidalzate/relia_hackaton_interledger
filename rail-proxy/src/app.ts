import { createHash, timingSafeEqual } from 'node:crypto';
import Fastify, { type FastifyError } from 'fastify';
import rateLimit from '@fastify/rate-limit';
import type { Logger } from 'pino';
import { amountSchema, type Amount } from './amount.js';
import { PROBLEM_CONTENT_TYPE, ProblemError } from './problem.js';
import type { CreatePaymentRequest, PaymentService } from './payments.js';
import type { ReliaGrantService } from './relia-grant.js';
import { PAYMENT_POINTER_PATTERN, type ResolvedWallet, type WalletResolver } from './wallets.js';

export interface AppDeps {
  apiKey: string;
  logger: Logger;
  healthRateLimitPerMinute: number;
  reliaWallet: ResolvedWallet;
  reliaGrants: Pick<ReliaGrantService, 'status' | 'view' | 'request'>;
  wallets: Pick<WalletResolver, 'resolve'>;
  payments: Pick<PaymentService, 'create' | 'get'>;
}

const createPaymentSchema = {
  headers: {
    type: 'object',
    required: ['idempotency-key'],
    properties: { 'idempotency-key': { type: 'string', format: 'uuid' } },
  },
  body: {
    type: 'object',
    required: ['direction', 'counterparty', 'receiveAmount'],
    additionalProperties: false,
    properties: {
      direction: { type: 'string', enum: ['outgoing', 'incoming'] },
      counterparty: { type: 'string', pattern: PAYMENT_POINTER_PATTERN.source },
      receiveAmount: amountSchema,
      expiresAt: { type: 'string', format: 'date-time' },
      description: { type: 'string', maxLength: 140 },
    },
  },
} as const;

const paymentIdSchema = {
  params: {
    type: 'object',
    required: ['paymentId'],
    properties: { paymentId: { type: 'string', format: 'uuid' } },
  },
} as const;

declare module 'fastify' {
  interface FastifyContextConfig {
    /** Ruta sin `X-Api-Key`. */
    public?: boolean;
  }
}

export async function buildApp(deps: AppDeps) {
  const app = Fastify({
    loggerInstance: deps.logger,
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
      const reliaGrant = deps.reliaGrants.status();
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

  app.post<{ Headers: { 'idempotency-key': string }; Body: CreatePaymentRequest }>(
    '/payments',
    { schema: createPaymentSchema },
    async (request, reply) => {
      const { payment, created } = await deps.payments.create(request.headers['idempotency-key'], request.body);
      if (!created) return reply.status(200).send(payment);
      return reply.status(201).header('Location', `/payments/${payment.id}`).send(payment);
    },
  );

  app.get<{ Params: { paymentId: string } }>('/payments/:paymentId', { schema: paymentIdSchema }, async (request) =>
    deps.payments.get(request.params.paymentId),
  );

  app.get('/relia-grant', async () => deps.reliaGrants.view());

  app.post<{ Body: { debitLimit: Amount; interval: string } }>(
    '/relia-grant',
    {
      schema: {
        body: {
          type: 'object',
          required: ['debitLimit', 'interval'],
          additionalProperties: false,
          properties: { debitLimit: amountSchema, interval: { type: 'string', minLength: 1 } },
        },
      },
    },
    async (request, reply) => {
      const grant = await deps.reliaGrants.request(request.body.debitLimit, request.body.interval);
      return reply.status(202).send(grant);
    },
  );

  return app;
}

// Comparar huellas de igual largo permite timingSafeEqual sin filtrar la longitud de la clave.
function digest(value: string): Buffer {
  return createHash('sha256').update(value).digest();
}
