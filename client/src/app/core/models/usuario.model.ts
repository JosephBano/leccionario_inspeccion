/**
 * Modelo del usuario autenticado.
 * Espejo de `UsuarioDto` en backend (`src/Leccionario.Api/Application/Authenticacion/Auth/AuthDtos.cs`).
 */
export interface UsuarioDto {
  idSigafi: string;
  nombre: string;
  email: string | null;
  tipoUsuario: string;
  roles: string[];
}

/**
 * Permisos derivados del JWT — espejo de `PermisosDto`.
 */
export interface PermisosDto {
  puedeEditarAsistencia: boolean;
  puedeCerrarSesion: boolean;
  puedeReabrirSesion: boolean;
  puedeEliminarAsistencia: boolean;
  puedeDescargarReportes: boolean;
}

/**
 * Fila de `/api/auth/me` — perfil + paralelos + permisos.
 */
export interface MiPerfilDto {
  usuario: UsuarioDto;
  paralelos: ParaleloResumenDto[];
  permisos: PermisosDto;
}

/**
 * Resumen de un paralelo del distributivo del docente.
 * Espejo de `ParaleloResumenDto`.
 */
export interface ParaleloResumenDto {
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
}
