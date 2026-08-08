import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

import { MiHorarioStore } from './mi-horario.store';
import type { BloqueMiHorario } from '@features/docente/models/mi-horario.model';

const DIAS = ['Lun', 'Mar', 'Mié', 'Jue', 'Vie', 'Sáb', 'Dom'] as const;

@Component({
  selector: 'app-mi-horario-page',
  providers: [MiHorarioStore],
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatTooltipModule],
  template: `
    <header class="page-header">
      <h1>Mi horario</h1>
      <div class="semana">
        <button mat-icon-button (click)="store.cambiarSemana(-1)" aria-label="Semana anterior">
          <mat-icon>chevron_left</mat-icon>
        </button>
        <span class="rango">{{ store.lunes() }} → {{ store.domingo() }}</span>
        <button mat-icon-button (click)="store.cambiarSemana(1)" aria-label="Semana siguiente">
          <mat-icon>chevron_right</mat-icon>
        </button>
        <button mat-stroked-button (click)="store.irAEstaSemana()">Esta semana</button>
      </div>
    </header>

    @if (store.cargando()) {
      <p class="estado">Cargando tu horario…</p>
    } @else if (store.error()) {
      <p class="estado error">{{ store.error() }}</p>
    } @else if (store.filas().length === 0) {
      <p class="estado">No tienes clases planificadas en esta semana.</p>
    } @else {
      <div class="scroll">
        <table class="grid">
          <thead>
            <tr>
              <th scope="col" class="col-hora">Hora</th>
              @for (d of dias; track d.iso) {
                <th scope="col">{{ d.etiqueta }}<br /><small>{{ d.iso }}</small></th>
              }
            </tr>
          </thead>
          <tbody>
            @for (fila of store.filas(); track fila.horaInicio + '-' + fila.horaFin) {
              <tr>
                <th scope="row" class="col-hora">{{ fila.horaInicio }}–{{ fila.horaFin }}</th>
                @for (celdasDia of fila.celdas; track $index) {
                  <td>
                    @for (celda of celdasDia; track celda.idHorarioInicio + '-' + celda.idAsignacion) {
                      <button
                        type="button"
                        class="bloque"
                        [class]="'estado-' + celda.estado.toLowerCase()"
                        [disabled]="celda.estado === 'Futura'"
                        [attr.aria-disabled]="celda.estado === 'Futura'"
                        [matTooltip]="tooltip(celda)"
                        (click)="abrir(celda)"
                      >
                        <span class="paralelo">{{ celda.tipoLicencia }} · {{ celda.paralelo }}</span>
                        <span class="jornada">{{ celda.jornada }}</span>
                        @if (celda.estado === 'Pendiente' && celda.diasRetraso > 0) {
                          <span class="retraso">{{ celda.diasRetraso }} d</span>
                        }
                      </button>
                    }
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: [`
    .page-header { display: flex; flex-wrap: wrap; align-items: center; gap: 16px; margin-bottom: 16px; }
    .semana { display: flex; align-items: center; gap: 8px; }
    .rango { font-variant-numeric: tabular-nums; }
    .estado { padding: 24px; text-align: center; color: rgb(0 0 0 / 60%); }
    .estado.error { color: #c5211f; }
    .scroll { overflow-x: auto; }
    .grid { width: 100%; border-collapse: collapse; }
    .grid th, .grid td { border: 1px solid rgb(0 0 0 / 12%); padding: 4px; vertical-align: top; }
    .grid thead th { font-weight: 500; font-size: 0.85rem; }
    .col-hora { white-space: nowrap; font-variant-numeric: tabular-nums; font-size: 0.8rem; }
    .bloque {
      display: flex; flex-direction: column; gap: 2px; width: 100%;
      border: 0; border-radius: 6px; padding: 6px 8px; cursor: pointer;
      font: inherit; text-align: left;
    }
    .bloque:disabled { cursor: default; }
    .paralelo { font-weight: 600; }
    .jornada, .retraso { font-size: 0.75rem; }
    .estado-pendiente { background: #fff3e0; color: #ef6c00; }
    .estado-borrador { background: #e3f2fd; color: #1565c0; }
    .estado-cerrada { background: #e8f5e9; color: #2e7d32; }
    .estado-futura { background: #f5f5f5; color: rgb(0 0 0 / 45%); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MiHorarioPage {
  protected readonly store = inject(MiHorarioStore);
  private readonly router = inject(Router);

  constructor() {
    this.store.irAEstaSemana();
  }

  protected get dias(): { iso: string; etiqueta: string }[] {
    return this.store.dias().map((iso, i) => ({ iso, etiqueta: DIAS[i] }));
  }

  protected tooltip(b: BloqueMiHorario): string {
    if (b.estado === 'Futura') return 'Todavía no puedes pasar lista de esta clase.';
    if (b.estado === 'Cerrada') return 'Sesión cerrada. Abre para consultar.';
    return `${b.asignatura} — pasar lista`;
  }

  protected abrir(b: BloqueMiHorario): void {
    if (b.estado === 'Futura') return;
    void this.router.navigate(['/paralelos', b.idAsignacion, 'pasar-lista'], {
      queryParams: { idHorarioInicio: b.idHorarioInicio },
    });
  }
}
