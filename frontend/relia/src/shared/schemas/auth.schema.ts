// zod/mini: misma validación, tree-shakable (`import { z }` mete ~400kB al bundle inicial)
import * as z from 'zod/mini';

export interface LoginRequest {
  username: string;
  password: string;
}

// Con cookie HttpOnly el backend no devuelve token; si lo devuelve se guarda solo en memoria
export const LoginResponseSchema = z.object({
  token: z.optional(z.string()),
});
