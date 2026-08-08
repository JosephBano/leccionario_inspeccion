import { Injectable, computed, inject, signal } from '@angular/core';
import { HorariosService } from '../../services/horarios.service';
import type {
  GridDto,
  ResultadoConflictoDto,
  AsignacionParaleloDto,
  DiaGridDto,
  FranjaDto,
  CeldaGridDto,
  CrearCeldaDto,
  SolicitudConflictoDto,
  PeriodoDto} from '../../models/horario.model';
import {
  obtenerMensajeError,
} from '../../models/horario.model';
import type { ParaleloClave, ParaleloOpcion } from '@shared/paralelo-selector/paralelo-selector';

export function getMondayOf(d: Date): string {
  const date = new Date(d);
  const day = date.getDay();
  const diff = date.getDate() - day + (day === 0 ? -6 : 1); // Adjust when day is Sunday (0)
  date.setDate(diff);
  return date.toISOString().split('T')[0];
}

@Injectable()
export class HorariosStore {
  private readonly horariosService = inject(HorariosService);

  // Private writable signals
  private readonly _clave = signal<ParaleloClave | null>(null);
  private readonly _lunes = signal<string>(getMondayOf(new Date()));
  private readonly _grid = signal<GridDto | null>(null);
  private readonly _periodos = signal<PeriodoDto[]>([]);
  private readonly _paralelos = signal<ParaleloOpcion[]>([]);
  private readonly _asignaciones = signal<AsignacionParaleloDto[]>([]);
  private readonly _cargando = signal<boolean>(false);
  private readonly _error = signal<string | null>(null);

  private readonly _celdaSeleccionada = signal<{ dia: DiaGridDto; franja: FranjaDto; celda?: CeldaGridDto } | null>(null);
  private readonly _resultadoConflicto = signal<ResultadoConflictoDto | null>(null);
  private readonly _cargandoConflicto = signal<boolean>(false);
  private readonly _cargandoGuardar = signal<boolean>(false);

  // Public readonly selectors
  readonly clave = this._clave.asReadonly();
  readonly lunes = this._lunes.asReadonly();
  readonly grid = this._grid.asReadonly();
  readonly periodos = this._periodos.asReadonly();
  readonly paralelos = this._paralelos.asReadonly();
  readonly asignaciones = this._asignaciones.asReadonly();
  readonly cargando = this._cargando.asReadonly();
  readonly error = this._error.asReadonly();

  readonly celdaSeleccionada = this._celdaSeleccionada.asReadonly();
  readonly resultadoConflicto = this._resultadoConflicto.asReadonly();
  readonly cargandoConflicto = this._cargandoConflicto.asReadonly();
  readonly cargandoGuardar = this._cargandoGuardar.asReadonly();

  readonly panelAbierto = computed(() => !!this._celdaSeleccionada());

  readonly puedeGuardarEnPanel = computed(() => {
    const celda = this._celdaSeleccionada();
    if (!celda) return false;
    const conf = this._resultadoConflicto();
    if (conf && conf.bloqueantes && conf.bloqueantes.length > 0) return false;
    return true;
  });

  inicializar(queryParams: { idPeriodo?: string; idNivel?: string; idSeccion?: string; idModalidad?: string; paralelo?: string; lunes?: string }): void {
    if (queryParams.lunes) {
      this._lunes.set(queryParams.lunes);
    }

    if (
      queryParams.idPeriodo &&
      queryParams.idNivel &&
      queryParams.idSeccion &&
      queryParams.idModalidad &&
      queryParams.paralelo
    ) {
      const claveFromParams: ParaleloClave = {
        idPeriodo: queryParams.idPeriodo,
        idNivel: Number(queryParams.idNivel),
        idSeccion: Number(queryParams.idSeccion),
        idModalidad: Number(queryParams.idModalidad),
        paralelo: queryParams.paralelo.trim(),
      };
      this._clave.set(claveFromParams);
    }

    this.cargarPeriodos(true);
    this.cargarParalelos(queryParams.idPeriodo, true);
  }

  cargarPeriodos(soloVigentes: boolean = true): void {
    this.horariosService.obtenerPeriodos(soloVigentes).subscribe({
      next: (data) => this._periodos.set(data),
      error: () => this._periodos.set([]),
    });
  }

  cargarParalelos(idPeriodo?: string, soloVigentes: boolean = true): void {
    this._cargando.set(true);
    this.horariosService.obtenerParalelosInspector(idPeriodo, soloVigentes).subscribe({
      next: (data) => {
        const opciones: ParaleloOpcion[] = data.map((p) => ({
          idPeriodo: p.idPeriodo,
          idNivel: p.idNivel,
          idSeccion: p.idSeccion,
          idModalidad: p.idModalidad,
          paralelo: p.paralelo,
          licencia: p.licencia,
          jornada: p.jornada,
          modalidad: p.modalidad,
          asignaciones: p.asignaciones,
        }));
        this._paralelos.set(opciones);

        // If no clave set yet, auto-select first option if available
        if (!this._clave() && opciones.length > 0) {
          const first = opciones[0];
          this.seleccionarParalelo({
            idPeriodo: first.idPeriodo,
            idNivel: first.idNivel,
            idSeccion: first.idSeccion,
            idModalidad: first.idModalidad,
            paralelo: first.paralelo,
          });
        } else if (this._clave()) {
          this.cargarGrid();
          this.cargarAsignaciones();
        } else {
          this._cargando.set(false);
        }
      },
      error: () => {
        this._cargando.set(false);
        this._error.set('Error al cargar paralelos.');
      },
    });
  }

  seleccionarParalelo(c: ParaleloClave): void {
    this._clave.set(c);
    this.cerrarPanel();
    this.cargarGrid();
    this.cargarAsignaciones();
  }

  cambiarSemana(deltaSemanas: number): void {
    const cur = new Date(this._lunes());
    cur.setDate(cur.getDate() + deltaSemanas * 7);
    const newLunes = getMondayOf(cur);
    this._lunes.set(newLunes);
    this.cerrarPanel();
    if (this._clave()) {
      this.cargarGrid();
    }
  }

  cargarGrid(): void {
    const clv = this._clave();
    if (!clv) return;

    this._cargando.set(true);
    this._error.set(null);
    this.horariosService.obtenerGrid(clv, this._lunes()).subscribe({
      next: (gridData) => {
        this._grid.set(gridData);
        this._cargando.set(false);
      },
      error: (err) => {
        this._cargando.set(false);
        const code = err?.error?.codigo || err?.error?.code;
        this._error.set(obtenerMensajeError(code));
      },
    });
  }

  cargarAsignaciones(): void {
    const clv = this._clave();
    if (!clv) return;

    this.horariosService.obtenerAsignaciones(clv).subscribe({
      next: (asigs) => {
        this._asignaciones.set(asigs);
      },
      error: () => {
        this._asignaciones.set([]);
      },
    });
  }

  abrirPanelCelda(dia: DiaGridDto, franja: FranjaDto, celda?: CeldaGridDto): void {
    if (!dia.habilitado) return;
    this._celdaSeleccionada.set({ dia, franja, celda });
    this._resultadoConflicto.set(null);

    if (celda) {
      this.validarConflicto(celda.idAsignacion);
    }
  }

  cerrarPanel(): void {
    this._celdaSeleccionada.set(null);
    this._resultadoConflicto.set(null);
  }

  validarConflicto(idAsignacion: number): void {
    const item = this._celdaSeleccionada();
    if (!item || !item.dia.idFecha) return;

    this._cargandoConflicto.set(true);
    const req: SolicitudConflictoDto = {
      idAsignacion,
      idFecha: item.dia.idFecha,
      idhora: item.franja.idhora,
      idHorarioExcluir: item.celda ? item.celda.idHorario : null,
    };

    this.horariosService.validarConflicto(req).subscribe({
      next: (res) => {
        this._cargandoConflicto.set(false);
        this._resultadoConflicto.set(res);
      },
      error: () => {
        this._cargandoConflicto.set(false);
      },
    });
  }

  guardarCelda(idAsignacion: number, confirmarAdvertencias: boolean = false): void {
    const item = this._celdaSeleccionada();
    if (!item || !item.dia.idFecha) return;

    this._cargandoGuardar.set(true);
    const req: CrearCeldaDto = {
      idAsignacion,
      idFecha: item.dia.idFecha,
      idhora: item.franja.idhora,
      confirmarAdvertencias,
    };

    const isEdit = !!item.celda;
    const obs$ = isEdit
      ? this.horariosService.actualizarCelda(item.celda!.idHorario, req)
      : this.horariosService.crearCelda(req);

    obs$.subscribe({
      next: () => {
        this._cargandoGuardar.set(false);
        this.cerrarPanel();
        this.cargarGrid();
      },
      error: (err) => {
        this._cargandoGuardar.set(false);
        const code = err?.error?.codigo || err?.error?.code;
        this._error.set(obtenerMensajeError(code));
      },
    });
  }

  eliminarCelda(idHorario: number): void {
    this._cargandoGuardar.set(true);
    this.horariosService.desactivarCelda(idHorario).subscribe({
      next: () => {
        this._cargandoGuardar.set(false);
        this.cerrarPanel();
        this.cargarGrid();
      },
      error: (err) => {
        this._cargandoGuardar.set(false);
        const code = err?.error?.codigo || err?.error?.code;
        this._error.set(obtenerMensajeError(code));
      },
    });
  }
}
