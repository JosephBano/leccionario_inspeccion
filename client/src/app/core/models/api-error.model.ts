/**
 * Forma de error uniforme que devuelve el backend.
 * Ver `docs/04-contrato-api.md` sección "Formato de error".
 * El cliente NUNCA decide por `mensaje` — siempre por `codigo`.
 */
export interface ApiError {
  codigo: string;
  mensaje: string;
  detalles: Record<string, unknown> | null;
  traceId: string;
}

/**
 * Códigos de error que el frontend sabe manejar (no exhaustivo).
 * Cualquier `codigo` fuera de este mapa se renderiza como error genérico.
 */
export const ErrorCodes = {
  VALIDACION: 'VALIDACION',
  CREDENCIALES_INVALIDAS: 'CREDENCIALES_INVALIDAS',
  TOKEN_EXPIRADO: 'TOKEN_EXPIRADO',
  CUENTA_INACTIVA: 'CUENTA_INACTIVA',
  SIN_ACCESO_SISTEMA: 'SIN_ACCESO_SISTEMA',
  DISTRIBUTIVO_AJENO: 'DISTRIBUTIVO_AJENO',
  SESION_CERRADA: 'SESION_CERRADA',
  FUERA_DE_PLAZO: 'FUERA_DE_PLAZO',
  NO_ENCONTRADO: 'NO_ENCONTRADO',
  SESION_DUPLICADA: 'SESION_DUPLICADA',
  MATRICULA_AJENA: 'MATRICULA_AJENA',
  FUERA_DE_VENTANA: 'FUERA_DE_VENTANA',
  DEMASIADAS_PETICIONES: 'DEMASIADAS_PETICIONES',
  ERROR_INTERNO: 'ERROR_INTERNO',
} as const;

export type ErrorCode = (typeof ErrorCodes)[keyof typeof ErrorCodes];
