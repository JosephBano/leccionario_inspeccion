import { Injectable, computed, inject, signal } from '@angular/core';
import { MiHorarioService } from '@features/docente/services/mi-horario.service';
import type { BloqueMiHorario } from '@features/docente/models/mi-horario.model';

/** Una franja horaria con sus siete celdas, de lunes a domingo. */
export interface FilaHorario {
  readonly horaInicio: string;
  readonly horaFin: string;
  readonly celdas: readonly (BloqueMiHorario | null)[];
}

/** Lunes de la semana que contiene `fechaISO`, en formato ISO. */
export function lunesDe(fechaISO: string): string {
  const d = new Date(`${fechaISO}T00:00:00`);
  const diaLunes0 = (d.getDay() + 6) % 7;
  d.setDate(d.getDate() - diaLunes0);
  return aISO(d);
}

function aISO(d: Date): string {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

function sumarDias(fechaISO: string, dias: number): string {
  const d = new Date(`${fechaISO}T00:00:00`);
  d.setDate(d.getDate() + dias);
  return aISO(d);
}

@Injectable()
export class MiHorarioStore {
  private readonly api = inject(MiHorarioService);

  private readonly _lunes = signal<string>(lunesDe(aISO(new Date())));
  private readonly _bloques = signal<BloqueMiHorario[]>([]);
  private readonly _cargando = signal(false);
  private readonly _error = signal<string | null>(null);

  readonly lunes = this._lunes.asReadonly();
  readonly bloques = this._bloques.asReadonly();
  readonly cargando = this._cargando.asReadonly();
  readonly error = this._error.asReadonly();

  readonly domingo = computed(() => sumarDias(this._lunes(), 6));

  /** Los siete días de la semana mostrada, en ISO. */
  readonly dias = computed(() =>
    Array.from({ length: 7 }, (_, i) => sumarDias(this._lunes(), i)),
  );

  /** Bloques agrupados: una fila por franja distinta, una columna por día. */
  readonly filas = computed<FilaHorario[]>(() => {
    const dias = this.dias();
    const porFranja = new Map<string, FilaHorario>();

    for (const b of this._bloques()) {
      const clave = `${b.horaInicio}-${b.horaFin}`;
      let fila = porFranja.get(clave);
      if (!fila) {
        fila = { horaInicio: b.horaInicio, horaFin: b.horaFin, celdas: Array(7).fill(null) };
        porFranja.set(clave, fila);
      }
      const col = dias.indexOf(b.fecha);
      if (col >= 0) (fila.celdas as (BloqueMiHorario | null)[])[col] = b;
    }

    return [...porFranja.values()].sort((a, b) => a.horaInicio.localeCompare(b.horaInicio));
  });

  /** Posiciona la semana en la que cae `fechaISO` y carga. */
  irASemanaDe(fechaISO: string): void {
    this._lunes.set(lunesDe(fechaISO));
    this.cargar();
  }

  irAEstaSemana(): void {
    this.irASemanaDe(aISO(new Date()));
  }

  cambiarSemana(deltaSemanas: number): void {
    this._lunes.set(sumarDias(this._lunes(), deltaSemanas * 7));
    this.cargar();
  }

  cargar(): void {
    this._cargando.set(true);
    this._error.set(null);
    this.api.obtener(this._lunes(), this.domingo()).subscribe({
      next: (bloques) => {
        this._bloques.set(bloques);
        this._cargando.set(false);
      },
      error: () => {
        this._bloques.set([]);
        this._error.set('No se pudo cargar tu horario. Intenta de nuevo.');
        this._cargando.set(false);
      },
    });
  }
}
