import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatIconModule } from '@angular/material/icon';
import type { FranjaDto, DiaGridDto, CeldaGridDto } from '../../../models/horario.model';

export interface CeldaClickEvent {
  dia: DiaGridDto;
  franja: FranjaDto;
  celda?: CeldaGridDto;
}

@Component({
  selector: 'app-horario-grid',
  standalone: true,
  imports: [CommonModule, MatTooltipModule, MatIconModule],
  template: `
    <div class="grid-table-wrapper">
      <table class="grid-table" aria-label="Matriz de horario semanal">
        <thead>
          <tr>
            <th class="col-franja">Franja</th>
            @for (d of dias(); track trackDia(d)) {
              <th
                class="col-dia"
                [class.deshabilitado]="!d.habilitado"
                [matTooltip]="d.motivo || ''"
                [matTooltipDisabled]="d.habilitado"
              >
                <div class="dia-nombre">{{ d.dia }}</div>
                <div class="dia-fecha">{{ d.fecha }}</div>
                @if (!d.habilitado) {
                  <span class="badge-deshabilitado">Inactivo</span>
                }
              </th>
            }
          </tr>
        </thead>
        <tbody>
          @for (f of franjas(); track f.idhora) {
            <tr>
              <td class="cell-franja">
                <div class="franja-time">{{ f.horaInicio }} – {{ f.horaFin }}</div>
                @if (f.tipo === 'X') {
                  <span class="badge-institucional" title="Franja Institucional">Inst.</span>
                }
              </td>
              @for (d of dias(); track trackDia(d)) {
                @let celda = obtenerCelda(d, f);
                @let estadoConflicto = celda ? celdaConflictos()[celda.idHorario] : null;
                <td
                  class="cell-slot"
                  [class.deshabilitado]="!d.habilitado"
                  [class.modo-lectura]="modo() === 'lectura'"
                  [class.has-celda]="!!celda"
                  [class.conflicto-bloqueante]="estadoConflicto === 'bloqueante'"
                  [class.conflicto-advertencia]="estadoConflicto === 'advertencia'"
                  [matTooltip]="(!d.habilitado ? d.motivo : '') || ''"
                  (click)="onCeldaClick(d, f, celda)"
                >
                  @if (celda) {
                    <div class="celda-content">
                      <div class="celda-docente">{{ celda.nombreDocente || 'Sin docente' }}</div>
                      @if (celda.tipoBloque) {
                        <div class="celda-bloque">{{ celda.tipoBloque }}</div>
                      }
                    </div>
                  } @else if (d.habilitado && modo() === 'edicion') {
                    <div class="celda-empty">
                      <mat-icon class="icon-add">add</mat-icon>
                    </div>
                  }
                </td>
              }
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    .grid-table-wrapper {
      width: 100%;
      overflow-x: auto;
      border: 1px solid var(--cplec-border, #e3e7ef);
      border-radius: 8px;
      box-shadow: 0 2px 4px rgba(0,0,0,0.04);
      background: #fff;
    }
    .grid-table {
      width: 100%;
      border-collapse: collapse;
      min-width: 800px;
      font-size: 0.9rem;
    }
    th, td {
      border: 1px solid #e5e7eb;
      text-align: center;
      padding: 8px 10px;
      vertical-align: middle;
    }
    th {
      background: #f8fafc;
      color: #1e293b;
      font-weight: 600;
    }
    .col-franja {
      width: 120px;
      background: #f1f5f9;
    }
    .col-dia {
      width: calc((100% - 120px) / 7);
    }
    .col-dia.deshabilitado {
      background: #f3f4f6;
      color: #9ca3af;
    }
    .dia-nombre {
      font-size: 1rem;
      text-transform: capitalize;
    }
    .dia-fecha {
      font-size: 0.8rem;
      color: #64748b;
      font-weight: normal;
    }
    .badge-deshabilitado {
      display: inline-block;
      font-size: 0.7rem;
      background: #e5e7eb;
      color: #6b7280;
      padding: 1px 4px;
      border-radius: 4px;
      margin-top: 2px;
    }
    .cell-franja {
      background: #f8fafc;
      font-weight: 500;
      color: #334155;
    }
    .franja-time {
      white-space: nowrap;
    }
    .badge-institucional {
      font-size: 0.65rem;
      background: #dbeafe;
      color: #1e40af;
      padding: 1px 4px;
      border-radius: 4px;
    }
    .cell-slot {
      height: 60px;
      cursor: pointer;
      transition: background-color 0.15s ease;
      position: relative;
    }
    .cell-slot:not(.deshabilitado):not(.modo-lectura):hover {
      background: #f0f9ff;
    }
    .cell-slot.deshabilitado {
      background: #f9fafb;
      cursor: not-allowed;
      opacity: 0.6;
    }
    .cell-slot.modo-lectura {
      cursor: default;
    }
    .cell-slot.has-celda {
      background: #eff6ff;
      border-left: 3px solid #3b82f6;
    }
    .cell-slot.conflicto-bloqueante {
      background: #fef2f2 !important;
      border: 2px solid #ef4444 !important;
    }
    .cell-slot.conflicto-advertencia {
      background: #fffbebfb !important;
      border: 2px solid #f59e0b !important;
    }
    .celda-content {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .celda-docente {
      font-weight: 600;
      color: #1e293b;
      font-size: 0.85rem;
      line-height: 1.2;
    }
    .celda-bloque {
      font-size: 0.75rem;
      color: #475569;
    }
    .celda-empty {
      color: #cbd5e1;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .icon-add {
      font-size: 20px;
      width: 20px;
      height: 20px;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HorarioGridComponent {
  readonly franjas = input<readonly FranjaDto[]>([]);
  readonly dias = input<readonly DiaGridDto[]>([]);
  readonly celdas = input<readonly CeldaGridDto[]>([]);
  readonly modo = input<'edicion' | 'lectura'>('edicion');
  readonly celdaConflictos = input<Record<number, 'bloqueante' | 'advertencia'>>({});

  readonly celdaClick = output<CeldaClickEvent>();

  protected trackDia(d: DiaGridDto): string {
    return d.fecha || d.dia;
  }

  protected obtenerCelda(dia: DiaGridDto, franja: FranjaDto): CeldaGridDto | undefined {
    return this.celdas().find(
      (c) => c.idhora === franja.idhora && (c.dia.toLowerCase() === dia.dia.toLowerCase())
    );
  }

  protected onCeldaClick(dia: DiaGridDto, franja: FranjaDto, celda?: CeldaGridDto): void {
    if (!dia.habilitado || this.modo() === 'lectura') {
      return;
    }
    this.celdaClick.emit({ dia, franja, celda });
  }
}
