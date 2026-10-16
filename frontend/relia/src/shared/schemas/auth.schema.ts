// zod/mini: misma validación, tree-shakable (`import { z }` mete ~400kB al bundle inicial)
import * as z from 'zod/mini';

export type Role = 'agent' | 'admin';

export interface LoginRequest {
  email: string;
  password: string;
}

// Con cookie HttpOnly el backend no devuelve token; si lo devuelve se guarda solo en memoria
export const LoginResponseSchema = z.object({
  role: z.enum(['agent', 'admin']),
  token: z.optional(z.string()),
});
export type LoginResponse = z.infer<typeof LoginResponseSchema>;
