import { z } from 'zod'

/** Mirrors the API password policy (ASP.NET Core Identity options + FluentValidation). */
export const passwordSchema = z
  .string()
  .min(8, 'Mínimo 8 caracteres.')
  .max(128, 'Máximo 128 caracteres.')
  .regex(/[a-z]/, 'Incluye al menos una minúscula.')
  .regex(/[A-Z]/, 'Incluye al menos una mayúscula.')
  .regex(/[0-9]/, 'Incluye al menos un número.')
