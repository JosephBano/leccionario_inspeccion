/**
 * Fila devuelta por `GET /api/mis-paralelos`.
 * Espejo de `MiParaleloDto` (`docs/04-contrato-api.md` sección Docente).
 */
export interface MiParaleloDto {
  idAsignacion: number;
  idPeriodo: string;
  asignatura: string;
  tipoLicencia: string;
  jornada: string;
  modalidad: string;
  paralelo: string;
  fechaInicial: string | null;
  fechaFin: string | null;
  totalAlumnos: number;
  sesionesRegistradas: number;
  ultimaSesion: string | null;
}

/**
 * Fila devuelta por `GET /api/paralelos/{idAsignacion}/alumnos`.
 * Espejo de `AlumnoNominaDto`.
 */
export interface AlumnoNominaDto {
  idMatricula: number;
  idAlumno: string;
  apellidos: string;
  nombres: string;
  retirado: boolean;
  esOyente: boolean;
}

/**
 * Fila devuelta por `GET /api/periodos/por-nivel`.
 * Espejo de `PeriodoPorNivelDto`.
 */
export interface PeriodoPorNivelDto {
  idNivel: number;
  tipoLicencia: string;
  idPeriodo: string;
  detalle: string | null;
  fechaInicial: string | null;
  fechaFin: string | null;
  vigencia: 'VIGENTE' | 'FUTURO' | 'CERRADO';
  asignaciones: number;
  docentes: number;
}
