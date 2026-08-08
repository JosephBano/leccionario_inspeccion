import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';

export interface ParaleloClave {
  idPeriodo: string;
  idNivel: number;
  idSeccion: number;
  idModalidad: number;
  paralelo: string;
}

export interface ParaleloOpcion extends ParaleloClave {
  licencia?: string | null;
  jornada?: string | null;
  modalidad?: string | null;
  asignaciones?: number;
}

export interface PeriodoSelectorItem {
  idPeriodo: string;
  detalle?: string | null;
}

interface NivelItem {
  idNivel: number;
  licencia: string;
}

interface ModalidadItem {
  idModalidad: number;
  nombre: string;
}

interface SeccionItem {
  idSeccion: number;
  nombre: string;
}

@Component({
  selector: 'app-paralelo-selector',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatFormFieldModule,
    MatSelectModule,
    MatIconModule,
  ],
  template: `
    <div class="selector-container">
      <!-- 1. Período -->
      <mat-form-field appearance="outline" class="select-field">
        <mat-label>1. Período</mat-label>
        <mat-select
          [ngModel]="idPeriodoSeleccionado()"
          (ngModelChange)="onPeriodoChange($event)"
          placeholder="Seleccione período..."
        >
          @for (p of periodos(); track p.idPeriodo) {
            <mat-option [value]="p.idPeriodo">
              {{ p.idPeriodo }}{{ p.detalle ? ' — ' + p.detalle : '' }}
            </mat-option>
          }
        </mat-select>
      </mat-form-field>

      <!-- 2. Nivel / Tipo de Licencia -->
      <mat-form-field appearance="outline" class="select-field">
        <mat-label>2. Nivel / Licencia</mat-label>
        <mat-select
          [ngModel]="idNivelSeleccionado()"
          (ngModelChange)="onNivelChange($event)"
          [disabled]="!idPeriodoSeleccionado() || nivelesDisponibles().length === 0"
          placeholder="Seleccione nivel..."
        >
          @for (n of nivelesDisponibles(); track n.idNivel) {
            <mat-option [value]="n.idNivel">
              {{ n.licencia }}
            </mat-option>
          }
        </mat-select>
      </mat-form-field>

      <!-- 3. Modalidad -->
      <mat-form-field appearance="outline" class="select-field">
        <mat-label>3. Modalidad</mat-label>
        <mat-select
          [ngModel]="idModalidadSeleccionada()"
          (ngModelChange)="onModalidadChange($event)"
          [disabled]="!idNivelSeleccionado() || modalidadesDisponibles().length === 0"
          placeholder="Seleccione modalidad..."
        >
          @for (m of modalidadesDisponibles(); track m.idModalidad) {
            <mat-option [value]="m.idModalidad">
              {{ m.nombre }}
            </mat-option>
          }
        </mat-select>
      </mat-form-field>

      <!-- 4. Jornada / Sección -->
      <mat-form-field appearance="outline" class="select-field">
        <mat-label>4. Jornada</mat-label>
        <mat-select
          [ngModel]="idSeccionSeleccionada()"
          (ngModelChange)="onSeccionChange($event)"
          [disabled]="!idModalidadSeleccionada() || seccionesDisponibles().length === 0"
          placeholder="Seleccione jornada..."
        >
          @for (s of seccionesDisponibles(); track s.idSeccion) {
            <mat-option [value]="s.idSeccion">
              {{ s.nombre }}
            </mat-option>
          }
        </mat-select>
      </mat-form-field>

      <!-- 5. Paralelo -->
      <mat-form-field appearance="outline" class="select-field select-field-sm">
        <mat-label>5. Paralelo</mat-label>
        <mat-select
          [ngModel]="paraleloSeleccionado()"
          (ngModelChange)="onParaleloChange($event)"
          [disabled]="!idSeccionSeleccionada() || paralelosFiltrados().length === 0"
          placeholder="Paralelo..."
        >
          @for (p of paralelosFiltrados(); track p) {
            <mat-option [value]="p">
              Paralelo "{{ p }}"
            </mat-option>
          }
        </mat-select>
      </mat-form-field>
    </div>
  `,
  styles: [`
    .selector-container {
      display: flex;
      flex-wrap: wrap;
      gap: 10px;
      width: 100%;
      max-width: 1200px;
    }
    .select-field {
      flex: 1 1 180px;
      min-width: 160px;
    }
    .select-field-sm {
      flex: 0 1 140px;
      min-width: 120px;
    }
    @media (max-width: 768px) {
      .select-field, .select-field-sm {
        flex: 1 1 100%;
        min-width: 100%;
      }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ParaleloSelector {
  readonly periodos = input<readonly PeriodoSelectorItem[]>([]);
  readonly opciones = input<readonly ParaleloOpcion[]>([]);
  readonly seleccion = input<ParaleloClave | null>(null);

  readonly periodoChange = output<string>();
  readonly seleccionChange = output<ParaleloClave>();

  protected readonly idPeriodoSeleccionado = signal<string | null>(null);
  protected readonly idNivelSeleccionado = signal<number | null>(null);
  protected readonly idModalidadSeleccionada = signal<number | null>(null);
  protected readonly idSeccionSeleccionada = signal<number | null>(null);
  protected readonly paraleloSeleccionado = signal<string | null>(null);

  constructor() {
    // Keep external selection signal synchronized
    effect(() => {
      const s = this.seleccion();
      if (s) {
        this.idPeriodoSeleccionado.set(s.idPeriodo);
        this.idNivelSeleccionado.set(s.idNivel);
        this.idModalidadSeleccionada.set(s.idModalidad);
        this.idSeccionSeleccionada.set(s.idSeccion);
        this.paraleloSeleccionado.set(s.paralelo.trim());
      } else {
        const pers = this.periodos();
        if (pers.length > 0 && !this.idPeriodoSeleccionado()) {
          this.idPeriodoSeleccionado.set(pers[0].idPeriodo);
        }
      }
    });

    // Auto-cascade choices when opciones update
    effect(() => {
      const ops = this.opciones();
      if (ops.length === 0) return;

      const idNivel = this.idNivelSeleccionado();
      if (!idNivel || !ops.some((o) => o.idNivel === idNivel)) {
        this.idNivelSeleccionado.set(ops[0].idNivel);
      }

      const currentNivel = this.idNivelSeleccionado();
      const opsNivel = ops.filter((o) => o.idNivel === currentNivel);
      const idMod = this.idModalidadSeleccionada();
      if (opsNivel.length > 0 && (!idMod || !opsNivel.some((o) => o.idModalidad === idMod))) {
        this.idModalidadSeleccionada.set(opsNivel[0].idModalidad);
      }

      const currentMod = this.idModalidadSeleccionada();
      const opsMod = opsNivel.filter((o) => o.idModalidad === currentMod);
      const idSec = this.idSeccionSeleccionada();
      if (opsMod.length > 0 && (!idSec || !opsMod.some((o) => o.idSeccion === idSec))) {
        this.idSeccionSeleccionada.set(opsMod[0].idSeccion);
      }

      const currentSec = this.idSeccionSeleccionada();
      const opsSec = opsMod.filter((o) => o.idSeccion === currentSec);
      const par = this.paraleloSeleccionado();
      if (opsSec.length > 0 && (!par || !opsSec.some((o) => o.paralelo.trim() === par))) {
        this.paraleloSeleccionado.set(opsSec[0].paralelo.trim());
      }
    });
  }

  protected readonly nivelesDisponibles = computed<NivelItem[]>(() => {
    const ops = this.opciones();
    const map = new Map<number, string>();
    for (const op of ops) {
      if (!map.has(op.idNivel)) {
        map.set(op.idNivel, op.licencia || `Nivel ${op.idNivel}`);
      }
    }
    return Array.from(map.entries())
      .map(([idNivel, licencia]) => ({ idNivel, licencia }))
      .sort((a, b) => a.licencia.localeCompare(b.licencia));
  });

  protected readonly modalidadesDisponibles = computed<ModalidadItem[]>(() => {
    const idNivel = this.idNivelSeleccionado();
    if (!idNivel) return [];
    const ops = this.opciones().filter((op) => op.idNivel === idNivel);
    const map = new Map<number, string>();
    for (const op of ops) {
      if (!map.has(op.idModalidad)) {
        map.set(op.idModalidad, op.modalidad || `Modalidad ${op.idModalidad}`);
      }
    }
    return Array.from(map.entries())
      .map(([idModalidad, nombre]) => ({ idModalidad, nombre }))
      .sort((a, b) => a.nombre.localeCompare(b.nombre));
  });

  protected readonly seccionesDisponibles = computed<SeccionItem[]>(() => {
    const idNivel = this.idNivelSeleccionado();
    const idModalidad = this.idModalidadSeleccionada();
    if (!idNivel || !idModalidad) return [];
    const ops = this.opciones().filter(
      (op) => op.idNivel === idNivel && op.idModalidad === idModalidad,
    );
    const map = new Map<number, string>();
    for (const op of ops) {
      if (!map.has(op.idSeccion)) {
        map.set(op.idSeccion, op.jornada || `Sección ${op.idSeccion}`);
      }
    }
    return Array.from(map.entries())
      .map(([idSeccion, nombre]) => ({ idSeccion, nombre }))
      .sort((a, b) => a.nombre.localeCompare(b.nombre));
  });

  protected readonly paralelosFiltrados = computed<string[]>(() => {
    const idNivel = this.idNivelSeleccionado();
    const idModalidad = this.idModalidadSeleccionada();
    const idSeccion = this.idSeccionSeleccionada();
    if (!idNivel || !idModalidad || !idSeccion) return [];

    const set = new Set<string>();
    for (const op of this.opciones()) {
      if (
        op.idNivel === idNivel &&
        op.idModalidad === idModalidad &&
        op.idSeccion === idSeccion
      ) {
        set.add(op.paralelo.trim());
      }
    }
    return Array.from(set).sort();
  });

  protected onPeriodoChange(idPeriodo: string): void {
    this.idPeriodoSeleccionado.set(idPeriodo);
    this.idNivelSeleccionado.set(null);
    this.idModalidadSeleccionada.set(null);
    this.idSeccionSeleccionada.set(null);
    this.paraleloSeleccionado.set(null);
    this.periodoChange.emit(idPeriodo);
  }

  protected onNivelChange(idNivel: number): void {
    this.idNivelSeleccionado.set(idNivel);
    this.idModalidadSeleccionada.set(null);
    this.idSeccionSeleccionada.set(null);
    this.paraleloSeleccionado.set(null);

    const opsNivel = this.opciones().filter((op) => op.idNivel === idNivel);
    if (opsNivel.length > 0) {
      const firstMod = opsNivel[0].idModalidad;
      this.idModalidadSeleccionada.set(firstMod);
      const opsMod = opsNivel.filter((op) => op.idModalidad === firstMod);
      if (opsMod.length > 0) {
        const firstSec = opsMod[0].idSeccion;
        this.idSeccionSeleccionada.set(firstSec);
        const opsSec = opsMod.filter((op) => op.idSeccion === firstSec);
        if (opsSec.length > 0) {
          const firstPar = opsSec[0].paralelo.trim();
          this.paraleloSeleccionado.set(firstPar);
          this.emitirSeleccionActual();
        }
      }
    }
  }

  protected onModalidadChange(idModalidad: number): void {
    this.idModalidadSeleccionada.set(idModalidad);
    this.idSeccionSeleccionada.set(null);
    this.paraleloSeleccionado.set(null);

    const idNivel = this.idNivelSeleccionado();
    const opsMod = this.opciones().filter(
      (op) => op.idNivel === idNivel && op.idModalidad === idModalidad,
    );

    if (opsMod.length > 0) {
      const firstSec = opsMod[0].idSeccion;
      this.idSeccionSeleccionada.set(firstSec);
      const opsSec = opsMod.filter((op) => op.idSeccion === firstSec);
      if (opsSec.length > 0) {
        const firstPar = opsSec[0].paralelo.trim();
        this.paraleloSeleccionado.set(firstPar);
        this.emitirSeleccionActual();
      }
    }
  }

  protected onSeccionChange(idSeccion: number): void {
    this.idSeccionSeleccionada.set(idSeccion);
    this.paraleloSeleccionado.set(null);

    const idNivel = this.idNivelSeleccionado();
    const idModalidad = this.idModalidadSeleccionada();
    const opsSec = this.opciones().filter(
      (op) =>
        op.idNivel === idNivel &&
        op.idModalidad === idModalidad &&
        op.idSeccion === idSeccion,
    );

    if (opsSec.length > 0) {
      const firstPar = opsSec[0].paralelo.trim();
      this.paraleloSeleccionado.set(firstPar);
      this.emitirSeleccionActual();
    }
  }

  protected onParaleloChange(paralelo: string): void {
    this.paraleloSeleccionado.set(paralelo);
    this.emitirSeleccionActual();
  }

  private emitirSeleccionActual(): void {
    const idPeriodo = this.idPeriodoSeleccionado();
    const idNivel = this.idNivelSeleccionado();
    const idModalidad = this.idModalidadSeleccionada();
    const idSeccion = this.idSeccionSeleccionada();
    const paralelo = this.paraleloSeleccionado();

    if (idPeriodo && idNivel && idModalidad && idSeccion && paralelo) {
      this.seleccionChange.emit({
        idPeriodo,
        idNivel,
        idModalidad,
        idSeccion,
        paralelo: paralelo.trim(),
      });
    }
  }
}
