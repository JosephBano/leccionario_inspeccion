export interface ParaleloDto {
  idPeriodo: string;
  idNivel: number;
  idSeccion: number;
  idModalidad: number;
  paralelo: string;
  licencia?: string | null;
  jornada?: string | null;
  modalidad?: string | null;
  asignaciones: number;
}

export interface FranjaDto {
  idhora: number;
  horaInicio: string;
  horaFin: string;
  minutos?: number;
  numeroHora?: number | null;
  tipo?: 'X' | 'Z';
  esEliminable?: boolean;
}

export interface CrearFranjaDto {
  horaInicio: string;
  horaFin: string;
  numeroHora?: number | null;
}

export interface DiaGridDto {
  dia: string;
  fecha: string;
  idFecha: number | null;
  habilitado: boolean;
  motivo: string | null;
}

export interface CeldaGridDto {
  idHorario: number;
  idAsignacion: number;
  idhora: number;
  dia: string;
  nombreDocente: string | null;
  tipoBloque: string | null;
}

export interface GridDto {
  franjas: FranjaDto[];
  dias: DiaGridDto[];
  celdas: CeldaGridDto[];
}

export interface SolicitudConflictoDto {
  idAsignacion: number;
  idFecha: number;
  idhora: number;
  idHorarioExcluir?: number | null;
}

export type SeveridadConflicto = 'Bloqueante' | 'Advertencia';

export interface ConflictoDto {
  tipo: string;
  severidad: SeveridadConflicto;
  mensaje: string;
  idHorarioConflicto: number;
  idProfesor?: string | null;
  carrera?: string | null;
  nivel?: string | null;
  paralelo?: string | null;
  franjaAjena?: string | null;
}

export interface ResultadoConflictoDto {
  bloqueantes: ConflictoDto[];
  advertencias: ConflictoDto[];
  hayBloqueantes?: boolean;
}

export interface CrearCeldaDto {
  idAsignacion: number;
  idFecha: number;
  idhora: number;
  tipoBloque?: string | null;
  confirmarAdvertencias?: boolean;
}

export interface CeldaCreadaDto {
  idHorario: number;
  advertencias: ConflictoDto[];
}

export interface OperacionRangoDto {
  idAsignacion: number;
  dia: string;
  idhora: number;
  desde: string;
  hasta: string;
  tipoBloque?: string | null;
  confirmarAdvertencias?: boolean;
}

export interface DetalleOperacionDto {
  fecha: string;
  exitoso: boolean;
  motivoFallo?: string | null;
  idHorario?: number | null;
}

export interface ResultadoRangoDto {
  totalProcesados: number;
  totalExitosos: number;
  totalFallidos: number;
  detalles: DetalleOperacionDto[];
  advertencia?: string | null;
}

export interface PeriodoDto {
  idPeriodo: string;
  detalle?: string | null;
  fechaInicial?: string | null;
  fechaFin?: string | null;
}

export interface AsignacionParaleloDto {
  idAsignacion: number;
  asignatura: string;
  idProfesor: string | null;
  nombreDocente: string | null;
  fechaInicial: string | null;
  fechaFin: string | null;
}

export interface SesionTardiaDto {
  idSesion: number;
  idAsignacion: number;
  fecha: string;
  nombreDocente: string | null;
  tema: string;
  diasRetraso: number;
}

export interface DiaSinRegistrarDto {
  idAsignacion: number;
  fecha: string;
  nombreDocente: string | null;
  numeroBloque: number;
  diasVencido: number;
}

export const MENSAJES_ERROR_HORARIO: Record<string, string> = {
  FUERA_DE_ALCANCE: 'Acceso denegado: el elemento no está bajo su alcance.',
  FRANJA_NO_PROPIA: 'La franja pertenece al instituto (tipo institucional) y no es editable.',
  CONFLICTO_HORARIO: 'Conflicto de horario detectado con otra clase asignada.',
  FRANJA_EN_USO: 'No se puede desactivar la franja porque está en uso en un horario activo.',
  SESION_FUTURA: 'No se puede registrar asistencia en un bloque horario futuro.',
  HORARIO_REQUERIDO: 'Se requiere seleccionar un bloque de horario para iniciar la sesión.',
  FECHA_SIN_CALENDARIO: 'La fecha seleccionada no existe en el calendario de horarios.',
  RANGO_INVALIDO: 'El rango de fechas no es válido. La fecha inicio debe ser menor o igual a la fecha fin.',
  RANGO_EXCEDE_TOPE: 'El rango de fechas no puede superar las 16 semanas (112 días).',
  LOTE_EXCEDE_TOPE: 'El lote de celdas no puede superar 500 filas de horario.',
  FUERA_DE_VENTANA: 'La fecha está fuera de la ventana de la asignatura (fecha inicial / fecha fin).',
};

export function obtenerMensajeError(codigo: string | undefined | null): string {
  if (!codigo) {
    return 'Ocurrió un error inesperado en la operación de horario.';
  }
  return MENSAJES_ERROR_HORARIO[codigo] ?? 'Ocurrió un error inesperado en el servidor.';
}
