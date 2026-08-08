import type { OnChanges} from '@angular/core';
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import type {
  AsignacionParaleloDto,
  CeldaGridDto,
  DiaGridDto,
  FranjaDto,
  ResultadoConflictoDto,
} from '../../../models/horario.model';

@Component({
  selector: 'app-asignacion-panel',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatSelectModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatCardModule,
  ],
  template: `
    <div class="panel-container">
      <div class="panel-header">
        <h3>
          <mat-icon>edit_calendar</mat-icon>
          {{ celdaExistente() ? 'Editar Celda' : 'Asignar Horario' }}
        </h3>
        <button mat-icon-button (click)="cerrar.emit()">
          <mat-icon>close</mat-icon>
        </button>
      </div>

      <div class="panel-body">
        <div class="info-badge" *ngIf="dia() && franja()">
          <span class="info-dia">{{ dia()?.dia }} {{ dia()?.fecha }}</span>
          <span class="info-franja">{{ franja()?.horaInicio }} – {{ franja()?.horaFin }}</span>
        </div>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Asignatura / Docente</mat-label>
          <mat-select
            [ngModel]="selectedIdAsignacion()"
            (ngModelChange)="onAsignacionSelect($event)"
            placeholder="Seleccione la asignatura..."
          >
            @for (a of asignaciones(); track a.idAsignacion) {
              <mat-option [value]="a.idAsignacion">
                <div class="asig-option-label">
                  <strong>{{ a.asignatura }}</strong>
                  <div class="asig-option-sub">
                    {{ a.nombreDocente || 'Sin docente asignado' }}
                    @if (a.fechaInicial && a.fechaFin) {
                      <span>({{ a.fechaInicial }} al {{ a.fechaFin }})</span>
                    } @else {
                      <span class="sin-ventana">(Sin ventana definida)</span>
                    }
                  </div>
                </div>
              </mat-option>
            }
          </mat-select>
        </mat-form-field>

        @if (cargandoConflicto()) {
          <div class="spinner-box">
            <mat-spinner diameter="24"></mat-spinner>
            <span>Validando conflictos de horario...</span>
          </div>
        }

        @if (resultadoConflicto(); as conf) {
          @if (conf.bloqueantes && conf.bloqueantes.length > 0) {
            <div class="alert alert-danger" role="alert">
              <div class="alert-title">
                <mat-icon>error</mat-icon>
                <span>Conflictos Bloqueantes Detectados</span>
              </div>
              <ul class="alert-list">
                @for (b of conf.bloqueantes; track b.idHorarioConflicto || $index) {
                  <li>
                    <strong>{{ b.tipo }}:</strong> {{ b.mensaje }}
                    @if (b.franjaAjena) {
                      <span>({{ b.franjaAjena }})</span>
                    }
                  </li>
                }
              </ul>
            </div>
          } @else if (conf.advertencias && conf.advertencias.length > 0) {
            <div class="alert alert-warning" role="alert">
              <div class="alert-title">
                <mat-icon>warning</mat-icon>
                <span>Advertencia de Choque Externo</span>
              </div>
              <p class="alert-desc">
                Esta asignación choca con el horario de otro sistema o carrera. Cplec no puede editarlo directamente, pero puede guardar si confirma.
              </p>
              <ul class="alert-list">
                @for (adv of conf.advertencias; track adv.idHorarioConflicto || $index) {
                  <li>
                    <strong>{{ adv.carrera || 'Otra carrera' }} ({{ adv.nivel || 'Nivel' }} - Paralelo {{ adv.paralelo }}):</strong>
                    {{ adv.mensaje }} [Franja: {{ adv.franjaAjena }}]
                  </li>
                }
              </ul>
            </div>
          }
        }
      </div>

      <div class="panel-footer">
        @if (celdaExistente()) {
          <button
            mat-stroked-button
            color="warn"
            type="button"
            [disabled]="cargandoGuardar()"
            (click)="eliminar.emit(celdaExistente()!.idHorario)"
          >
            <mat-icon>delete</mat-icon>
            Eliminar
          </button>
        }

        <div class="actions-right">
          <button mat-button type="button" (click)="cerrar.emit()">Cancelar</button>

          @if (tieneSoloAdvertencias()) {
            <button
              mat-raised-button
              class="btn-amber"
              type="button"
              [disabled]="cargandoGuardar() || !selectedIdAsignacion()"
              (click)="onGuardar(true)"
            >
              Guardar igual
            </button>
          } @else {
            <button
              mat-raised-button
              color="primary"
              type="button"
              [disabled]="!puedeGuardar() || cargandoGuardar()"
              (click)="onGuardar(false)"
            >
              Guardar
            </button>
          }
        </div>
      </div>
    </div>
  `,
  styles: [`
    .panel-container {
      display: flex;
      flex-direction: column;
      height: 100%;
      background: #fff;
      border-radius: 8px;
      box-shadow: 0 4px 12px rgba(0,0,0,0.15);
      padding: 16px;
      gap: 16px;
    }
    .panel-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      border-bottom: 1px solid #e2e8f0;
      padding-bottom: 8px;
    }
    .panel-header h3 {
      margin: 0;
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 1.1rem;
      color: #0f172a;
    }
    .panel-body {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 12px;
      overflow-y: auto;
    }
    .info-badge {
      display: flex;
      gap: 12px;
      background: #f1f5f9;
      padding: 8px 12px;
      border-radius: 6px;
      font-weight: 500;
      font-size: 0.9rem;
    }
    .info-dia { text-transform: capitalize; color: #1e293b; }
    .info-franja { color: #3b82f6; }
    .full-width { width: 100%; }
    .asig-option-label {
      display: flex;
      flex-direction: column;
    }
    .asig-option-sub {
      font-size: 0.8rem;
      color: #64748b;
    }
    .sin-ventana { color: #d97706; }
    .spinner-box {
      display: flex;
      align-items: center;
      gap: 10px;
      font-size: 0.85rem;
      color: #64748b;
    }
    .alert {
      padding: 12px;
      border-radius: 6px;
      font-size: 0.85rem;
    }
    .alert-danger {
      background: #fef2f2;
      border: 1px solid #fca5a5;
      color: #991b1b;
    }
    .alert-warning {
      background: #fffbeb;
      border: 1px solid #fcd34d;
      color: #92400e;
    }
    .alert-title {
      display: flex;
      align-items: center;
      gap: 6px;
      font-weight: 700;
      margin-bottom: 4px;
    }
    .alert-list {
      margin: 4px 0 0 16px;
      padding: 0;
    }
    .panel-footer {
      display: flex;
      justify-content: space-between;
      align-items: center;
      border-top: 1px solid #e2e8f0;
      padding-top: 12px;
    }
    .actions-right {
      display: flex;
      gap: 8px;
      margin-left: auto;
    }
    .btn-amber {
      background-color: #d97706 !important;
      color: #fff !important;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AsignacionPanelComponent implements OnChanges {
  readonly asignaciones = input<readonly AsignacionParaleloDto[]>([]);
  readonly celdaExistente = input<CeldaGridDto | null>(null);
  readonly dia = input<DiaGridDto | null>(null);
  readonly franja = input<FranjaDto | null>(null);
  readonly resultadoConflicto = input<ResultadoConflictoDto | null>(null);
  readonly cargandoConflicto = input<boolean>(false);
  readonly cargandoGuardar = input<boolean>(false);

  readonly asignacionChange = output<number>();
  readonly guardar = output<{ idAsignacion: number; confirmarAdvertencias: boolean }>();
  readonly eliminar = output<number>();
  readonly cerrar = output<void>();

  protected readonly selectedIdAsignacion = signal<number | null>(null);

  constructor() {
    // When celdaExistente changes or is provided, initialize selectedIdAsignacion
  }

  ngOnChanges(): void {
    const celda = this.celdaExistente();
    if (celda && !this.selectedIdAsignacion()) {
      this.selectedIdAsignacion.set(celda.idAsignacion);
    }
  }

  protected onAsignacionSelect(idAsignacion: number): void {
    this.selectedIdAsignacion.set(idAsignacion);
    this.asignacionChange.emit(idAsignacion);
  }

  protected readonly tieneBloqueantes = computed(() => {
    const res = this.resultadoConflicto();
    return !!(res && res.bloqueantes && res.bloqueantes.length > 0);
  });

  protected readonly tieneSoloAdvertencias = computed(() => {
    const res = this.resultadoConflicto();
    if (!res) return false;
    const hasBloq = res.bloqueantes && res.bloqueantes.length > 0;
    const hasAdv = res.advertencias && res.advertencias.length > 0;
    return !hasBloq && hasAdv;
  });

  protected readonly puedeGuardar = computed(() => {
    if (!this.selectedIdAsignacion()) return false;
    if (this.cargandoConflicto()) return false;
    return !this.tieneBloqueantes();
  });

  protected onGuardar(confirmarAdvertencias: boolean): void {
    const id = this.selectedIdAsignacion();
    if (id) {
      this.guardar.emit({ idAsignacion: id, confirmarAdvertencias });
    }
  }
}
