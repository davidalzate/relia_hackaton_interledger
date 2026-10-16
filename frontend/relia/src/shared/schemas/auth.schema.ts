// zod/mini: misma validación, tree-shakable (`import { z }` mete ~400kB al bundle inicial)
import * as z from 'zod/mini';

export type Role = 'agent' | 'admin';

export interface LoginRequest {
  email: string;
  password: string;
}

export const SessionUserSchema = z.object({
  role: z.enum(['agent', 'admin']),
  name: z.string(),
  email: z.string(),
  // Solo corresponsales: nombre del comercio
  store: z.optional(z.string()),
});
export type SessionUser = z.infer<typeof SessionUserSchema>;

// Con cookie HttpOnly el backend no devuelve token; si lo devuelve se guarda solo en memoria
export const LoginResponseSchema = z.object({
  user: SessionUserSchema,
  token: z.optional(z.string()),
});
export type LoginResponse = z.infer<typeof LoginResponseSchema>;
