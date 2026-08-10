import { Injectable, inject, signal } from '@angular/core';
import { AgendaService } from '../../services/agenda.service';
import type { BloqueAgendaDto } from '../../models/agenda.model';
import { HorariosService } from '@features/inspector/services/horarios.service';
import type { GridDto} from '@features/inspector/models/horario.model';
import { obtenerMensajeError } from '@features/inspector/models/horario.model';
import { lunesDe, domingoDe, sumarDiasISO } from '@core/utils/fechas';
import type { ParaleloClave } from '@shared/paralelo-selector/paralelo-selector';

@Injectable()
export class AgendaStore {
  private readonly agendaService = inject(AgendaService);
  private readonly horariosService = inject(HorariosService);

  private readonly _idAsignacion = signal<number | null>(null);
  private readonly _clave = signal<ParaleloClave | null>(null);
  private readonly _lunes = signal<string>(lunesDe(new Date()));
  private readonly _bloques = signal<BloqueAgendaDto[]>([]);
  private readonly _grid = signal<GridDto | null>(null);
  private readonly _cargando = signal<boolean>(false);
  private readonly _error = signal<string | null>(null);

  readonly idAsignacion = this._idAsignacion.asReadonly();
  readonly clave = this._clave.asReadonly();
  readonly lunes = this._lunes.asReadonly();
  readonly bloques = this._bloques.asReadonly();
  readonly grid = this._grid.asReadonly();
  readonly cargando = this._cargando.asReadonly();
  readonly error = this._error.asReadonly();

  inicializar(idAsignacion: number, clave?: ParaleloClave | null, lunes?: string): void {
    this._idAsignacion.set(idAsignacion);
    if (clave) this._clave.set(clave);
    if (lunes) this._lunes.set(lunes);
    this.cargarAgendaYGrid();
  }

  cambiarSemana(deltaSemanas: number): void {
    this._lunes.set(sumarDiasISO(this._lunes(), deltaSemanas * 7));
    this.cargarAgendaYGrid();
  }

  cargarAgendaYGrid(): void {
    const idAsig = this._idAsignacion();
    if (!idAsig) return;

    const lunesStr = this._lunes();
    const domingoStr = domingoDe(lunesStr);

    this._cargando.set(true);
    this._error.set(null);

    this.agendaService.obtenerAgenda(idAsig, lunesStr, domingoStr).subscribe({
      next: (bloques) => {
        this._bloques.set(bloques);
        this._cargando.set(false);
      },
      error: (err) => {
        this._cargando.set(false);
        const code = err?.error?.codigo || err?.error?.code;
        this._error.set(obtenerMensajeError(code));
      },
    });

    const clv = this._clave();
    if (clv) {
      this.horariosService.obtenerGrid(clv, lunesStr).subscribe({
        next: (g) => this._grid.set(g),
        error: () => this._grid.set(null),
      });
    }
  }
}
