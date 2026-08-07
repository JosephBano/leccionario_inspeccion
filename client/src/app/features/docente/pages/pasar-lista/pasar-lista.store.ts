import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { EstadoAsistencia, TODOS_ESTADOS, siguienteEstado } from '@features/docente/models/asistencia.model';
import type {
  EstadoAsistenciaValue,
  MarcaAsistenciaInput,
  RegistrarAsistenciaRequest,
  SesionDto,
} from '@features/docente/models/asistencia.model';
import type { AlumnoNominaDto } from '@features/docente/models/distributivo.model';
import { AsistenciaService } from '@features/docente/services/asistencia.service';
import { DistributivoService } from '@features/docente/services/distributivo.service';

/**
 * Estado local para la pantalla de pasar lista.
 *
 * Mantiene una copia mutable del estado de cada alumno. La fuente de verdad
 * sigue siendo el backend; aquí solo se trackean los cambios pendientes.
 *
 * Reglas (`docs/06 § Pantalla crítica`):
 *  - Todos arrancan en `presente`.
 *  - Un toque rota el estado (presente → ausente → atraso → justificado → presente).
 *  - Guardado explícito con un botón: una sola petición idempotente con toda la lista.
 *  - Si la petición falla, el estado local se conserva y se ofrece reintentar.
 *  - Salir con cambios pendientes pide confirmación.
 */
export interface MarcaLocal {
  readonly idMatricula: number;
  readonly alumno: AlumnoNominaDto;
  estado: EstadoAsistenciaValue;
  minutosAtraso: number | null;
  observacion: string | null;
  modificado: boolean;
}

@Injectable()
export class PasarListaStore {
  private readonly asistencia = inject(AsistenciaService);
  private readonly distributivo = inject(DistributivoService);
  private readonly destroyRef = inject(DestroyRef);

  // Inputs
  private readonly _idAsignacion = signal<number | null>(null);
  private readonly _sesion = signal<SesionDto | null>(null);

  // Estado local
  private readonly _marcas = signal<MarcaLocal[]>([]);
  private readonly _guardando = signal(false);
  private readonly _errorGuardar = signal<string | null>(null);
  private readonly _ultimoGuardado = signal<Date | null>(null);

  // Selectores públicos
  readonly idAsignacion = this._idAsignacion.asReadonly();
  readonly sesion = this._sesion.asReadonly();
  readonly guardando = this._guardando.asReadonly();
  readonly errorGuardar = this._errorGuardar.asReadonly();
  readonly ultimoGuardado = this._ultimoGuardado.asReadonly();

  readonly marcas = this._marcas.asReadonly();
  readonly totalAlumnos = computed(() => this._marcas().length);
  readonly totalPresentes = computed(
    () => this._marcas().filter((m) => m.estado === EstadoAsistencia.Presente).length,
  );
  readonly totalAusentes = computed(
    () => this._marcas().filter((m) => m.estado === EstadoAsistencia.Ausente).length,
  );
  readonly totalAtrasos = computed(
    () => this._marcas().filter((m) => m.estado === EstadoAsistencia.Atraso).length,
  );
  readonly totalJustificados = computed(
    () => this._marcas().filter((m) => m.estado === EstadoAsistencia.Justificado).length,
  );
  readonly hayCambios = computed(() =>
    this._marcas().some((m) => m.modificado),
  );

  /**
   * Carga inicial: pide la nómina y, si ya hay sesión persistida para el día
   * actual, precarga sus marcas. Si no, crea una sesión idempotente.
   */
  cargar(idAsignacion: number, fecha: string, temaInicial: string): void {
    this._idAsignacion.set(idAsignacion);
    this._errorGuardar.set(null);

    this.distributivo
      .alumnos(idAsignacion)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (alumnos) => {
          this._marcas.set(
            alumnos
              .filter((a) => !a.retirado)
              .map<MarcaLocal>((a) => ({
                idMatricula: a.idMatricula,
                alumno: a,
                estado: EstadoAsistencia.Presente,
                minutosAtraso: null,
                observacion: null,
                modificado: false,
              })),
          );

          // Crea (o recupera) la sesión del día. Es idempotente en el backend.
          this.asistencia
            .crearSesion(idAsignacion, { fecha, tema: temaInicial })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: (sesion) => this.aplicarSesion(sesion),
              error: () => this._errorGuardar.set('No se pudo abrir la sesión del día.'),
            });
        },
        error: () => this._errorGuardar.set('No se pudo cargar la nómina del paralelo.'),
      });
  }

  /**
   * Aplica una sesión existente (GET /api/sesiones/{idSesion}) sobre el store.
   * Sobrescribe el estado local con lo que diga el backend.
   */
  aplicarSesion(sesion: SesionDto): void {
    this._sesion.set(sesion);

    if (sesion.asistencias.length === 0) {
      // La precarga del backend (todos en `presente`) ya está cubierta en `cargar()`.
      return;
    }

    this._marcas.update((marcas) =>
      marcas.map((m) => {
        const remoto = sesion.asistencias.find((a) => a.idMatricula === m.idMatricula);
        if (!remoto) return m;
        return {
          ...m,
          estado: remoto.estado,
          minutosAtraso: remoto.minutosAtraso ?? null,
          observacion: remoto.observacion ?? null,
          modificado: false,
        };
      }),
    );
  }

  ciclarEstado(idMatricula: number): void {
    this._marcas.update((marcas) =>
      marcas.map((m) => {
        if (m.idMatricula !== idMatricula) return m;
        const nuevoEstado = siguienteEstado(m.estado);
        return {
          ...m,
          estado: nuevoEstado,
          minutosAtraso: nuevoEstado === EstadoAsistencia.Atraso ? (m.minutosAtraso ?? 5) : null,
          modificado: true,
        };
      }),
    );
  }

  /**
   * Marca explícita a un estado concreto (sin ciclar). Útil para un menú.
   */
  marcar(idMatricula: number, estado: EstadoAsistenciaValue): void {
    this._marcas.update((marcas) =>
      marcas.map((m) => {
        if (m.idMatricula !== idMatricula) return m;
        if (!TODOS_ESTADOS.includes(estado)) return m;
        return {
          ...m,
          estado,
          minutosAtraso: estado === EstadoAsistencia.Atraso ? (m.minutosAtraso ?? 5) : null,
          modificado: true,
        };
      }),
    );
  }

  /**
   * Persiste el estado local en una sola petición idempotente. Devuelve una
   * promesa: si resuelve, no quedan cambios pendientes; si rechaza, el estado
   * local se conserva para reintentar.
   */
  async guardar(): Promise<void> {
    const idSesion = this._sesion()?.idSesion;
    if (idSesion == null) {
      throw new Error('No hay sesión abierta.');
    }

    const marcas = this._marcas();
    const body: RegistrarAsistenciaRequest = {
      motivo: 'Pase de lista del docente',
      marcas: marcas.map<MarcaAsistenciaInput>((m) => ({
        idMatricula: m.idMatricula,
        estado: m.estado,
        minutosAtraso: m.estado === EstadoAsistencia.Atraso ? m.minutosAtraso : null,
        observacion: m.observacion,
      })),
    };

    this._guardando.set(true);
    this._errorGuardar.set(null);

    try {
      const sesion = await firstValueFrom(this.asistencia.registrarAsistencia(idSesion, body));
      this.aplicarSesion(sesion);
      this._ultimoGuardado.set(new Date());
    } catch {
      this._errorGuardar.set(
        'No se pudo guardar. Verifica tu conexión y reintenta — la lista se conservó.',
      );
      throw new Error('guardar-fallo');
    } finally {
      this._guardando.set(false);
    }
  }

  descartar(): void {
    this._marcas.update((marcas) => marcas.map((m) => ({ ...m, modificado: false })));
    this._errorGuardar.set(null);
  }
}
