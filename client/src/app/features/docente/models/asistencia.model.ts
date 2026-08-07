/**
 * Estados válidos de una marca de asistencia.
 * Espejo de `EstadoAsistencia` (backend).
 */
export const EstadoAsistencia = {
  Presente: 'presente',
  Ausente: 'ausente',
  Atraso: 'atraso',
  Justificado: 'justificado',
} as const;

export type EstadoAsistenciaValue = (typeof EstadoAsistencia)[keyof typeof EstadoAsistencia];

export const TODOS_ESTADOS: readonly EstadoAsistenciaValue[] = [
  EstadoAsistencia.Presente,
  EstadoAsistencia.Ausente,
  EstadoAsistencia.Atraso,
  EstadoAsistencia.Justificado,
] as const;

export function siguienteEstado(actual: EstadoAsistenciaValue): EstadoAsistenciaValue {
  const idx = TODOS_ESTADOS.indexOf(actual);
  return TODOS_ESTADOS[(idx + 1) % TODOS_ESTADOS.length] ?? EstadoAsistencia.Presente;
}

export const EstadoSesion = {
  Borrador: 'borrador',
  Cerrada: 'cerrada',
} as const;

export type EstadoSesionValue = (typeof EstadoSesion)[keyof typeof EstadoSesion];

/**
 * Marca persistida. Espejo de `MarcaSesionDto`.
 */
export interface MarcaSesionDto {
  idAsistencia: number;
  idMatricula: number;
  estado: EstadoAsistenciaValue;
  minutosAtraso: number | null;
  observacion: string | null;
}

/**
 * Fila devuelta por `GET /api/sesiones/{idSesion}`.
 * Espejo de `SesionDto`.
 */
export interface SesionDto {
  idSesion: number;
  idAsignacion: number;
  fecha: string;
  numeroBloque: number;
  tema: string;
  observacion: string | null;
  estado: EstadoSesionValue;
  fechaCierre: string | null;
  asistencias: MarcaSesionDto[];
}

/**
 * Marca que el docente envía al guardar.
 * El backend acepta este shape en `POST /api/sesiones/{idSesion}/asistencias`.
 */
export interface MarcaAsistenciaInput {
  idMatricula: number;
  estado: EstadoAsistenciaValue;
  minutosAtraso?: number | null;
  observacion?: string | null;
}

export interface RegistrarAsistenciaRequest {
  marcas: MarcaAsistenciaInput[];
  motivo?: string | null;
}

export interface CrearSesionRequest {
  fecha: string;
  tema: string;
  observacion?: string | null;
  numeroBloque?: number;
}
