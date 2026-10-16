// zod/mini: misma validación, tree-shakable (`import { z }` mete ~400kB al bundle inicial)
import * as z from 'zod/mini';

export type Role = 'agent' | 'admin';

export interface LoginRequest {
  email: string;
  password: string;
}

const RoleSchema = z.enum(['agent', 'admin']);

export const LoginResponseSchema = z.object({
  accessToken: z.string(),
  expiresIn: z.number(),
  role: RoleSchema,
  // Solo corresponsales
  agentId: z.optional(z.nullable(z.string())),
});
export type LoginResponse = z.infer<typeof LoginResponseSchema>;

// `GET /me`; del corresponsal por ahora solo se usa el nombre del comercio
export const MeSchema = z.object({
  role: RoleSchema,
  email: z.string(),
  name: z.string(),
  agent: z.optional(z.nullable(z.object({ id: z.string(), name: z.string() }))),
});

export interface SessionUser {
  role: Role;
  name: string;
  email: string;
  // Solo corresponsales: nombre del comercio
  store?: string;
}
