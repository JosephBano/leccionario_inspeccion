import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatRadioModule } from '@angular/material/radio';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';

import { HorariosService } from '../../../services/horarios.service';
import {
  obtenerMensajeError,
} from '../../../models/horario.model';
import type {
  OperacionRangoDto,
  ResultadoRangoDto,
  AsignacionParaleloDto,
  FranjaDto,
} from '../../../models/horario.model';
import type { ParaleloClave } from '@shared/paralelo-selector/paralelo-selector';

export interface ReplicarRangoData {
  clave: ParaleloClave;
  asignaciones: AsignacionParaleloDto[];
  franjas: FranjaDto[];
  lunesActual: string;
}

const DIAS_SEMANA = [
  'Lunes', 'Martes', 'Miercoles', 'Jueves', 'Viernes', 'Sabado', 'Domingo'
];

@Component({
  selector: 'app-replicar-rango-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatRadioModule,
    MatProgressSpinnerModule,
    MatTableModule,
  ],
  template: `
    <h2 mat-dialog-title class="dialog-title">
      <mat-icon color="primary">date_range</mat-icon>
      Operaciones por Rango de Fechas
    </h2>

    <mat-dialog-content class="dialog-content">
      @if (!resultado()) {
        <div class="form-container">
          <div class="field-row">
            <mat-form-field appearance="outline">
              <mat-label>Operación</mat-label>
              <mat-select [(ngModel)]="operacion">
                <mat-option value="replicar">Replicar celda en rango</mat-option>
                <mat-option value="actualizar">Actualizar celdas en rango</mat-option>
                <mat-option value="eliminar">Eliminar celdas en rango</mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Día de la Semana</mat-label>
              <mat-select [(ngModel)]="dia">
                @for (d of diasSemana; track d) {
                  <mat-option [value]="d">{{ d }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          </div>

          <div class="field-row">
            <mat-form-field appearance="outline">
              <mat-label>Asignatura</mat-label>
              <mat-select [(ngModel)]="idAsignacion">
                @for (a of data.asignaciones; track a.idAsignacion) {
                  <mat-option [value]="a.idAsignacion">
                    {{ a.asignatura }} ({{ a.nombreDocente || 'Sin docente' }})
                  </mat-option>
                }
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Franja Horaria</mat-label>
              <mat-select [(ngModel)]="idhora">
                @for (f of data.franjas; track f.idhora) {
                  <mat-option [value]="f.idhora">
                    {{ f.horaInicio }} – {{ f.horaFin }}
                  </mat-option>
                }
              </mat-select>
            </mat-form-field>
          </div>

          <div class="field-row">
            <mat-form-field appearance="outline">
              <mat-label>Fecha Desde</mat-label>
              <input matInput type="date" [(ngModel)]="desde" (change)="validarFechas()" />
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Fecha Hasta</mat-label>
              <input matInput type="date" [(ngModel)]="hasta" (change)="validarFechas()" />
            </mat-form-field>
          </div>

          @if (errorLocal()) {
            <div class="alert alert-danger" role="alert">
              <mat-icon>error</mat-icon>
              <span>{{ errorLocal() }}</span>
            </div>
          }
        </div>
      } @else {
        <div class="resultado-container">
          <div class="res-summary">
            <h3>Resultado de la Operación</h3>
            <div class="summary-chips">
              <span class="chip chip-total">Procesados: {{ resultado()?.totalProcesados }}</span>
              <span class="chip chip-success">Exitosos: {{ resultado()?.totalExitosos }}</span>
              <span class="chip chip-error">Fallidos: {{ resultado()?.totalFallidos }}</span>
            </div>
          </div>

          @if (resultado()?.advertencia) {
            <div class="alert alert-warning">
              <mat-icon>warning</mat-icon>
              <span><strong>Advertencia:</strong> {{ resultado()?.advertencia }}</span>
            </div>
          }

          <div class="table-scroll">
            <table mat-table [dataSource]="resultado()?.detalles || []" class="mat-elevation-z1">
              <ng-container matColumnDef="fecha">
                <th mat-header-cell *matHeaderCellDef>Fecha</th>
                <td mat-cell *matCellDef="let row">{{ row.fecha }}</td>
              </ng-container>

              <ng-container matColumnDef="estado">
                <th mat-header-cell *matHeaderCellDef>Estado</th>
                <td mat-cell *matCellDef="let row">
                  @if (row.exitoso) {
                    <span class="badge-success">Exitoso</span>
                  } @else {
                    <span class="badge-error">Fallido</span>
                  }
                </td>
              </ng-container>

              <ng-container matColumnDef="motivo">
                <th mat-header-cell *matHeaderCellDef>Detalle / Motivo</th>
                <td mat-cell *matCellDef="let row">
                  {{ row.motivoFallo ? (obtenerMensaje(row.motivoFallo)) : 'OK' }}
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
            </table>
          </div>
        </div>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      @if (!resultado()) {
        <button mat-button (click)="dialogRef.close(false)" [disabled]="cargando()">Cancelar</button>
        <button
          mat-raised-button
          color="primary"
          [disabled]="!formularioValido() || cargando()"
          (click)="ejecutarOperacion()"
        >
          @if (cargando()) {
            <mat-spinner diameter="18"></mat-spinner>
          } @else {
            Ejecutar
          }
        </button>
      } @else {
        <button mat-raised-button color="primary" (click)="dialogRef.close(true)">Cerrar y Actualizar</button>
      }
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-title {
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .dialog-content {
      min-width: 480px;
      max-width: 600px;
    }
    .form-container {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding-top: 8px;
    }
    .field-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }
    mat-form-field { width: 100%; }
    .alert {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 14px;
      border-radius: 6px;
      font-size: 0.85rem;
    }
    .alert-danger { background: #fef2f2; color: #991b1b; border: 1px solid #fca5a5; }
    .alert-warning { background: #fffbeb; color: #92400e; border: 1px solid #fcd34d; margin-bottom: 12px; }
    .resultado-container { display: flex; flex-direction: column; gap: 12px; }
    .summary-chips { display: flex; gap: 8px; margin-top: 6px; }
    .chip { padding: 4px 8px; border-radius: 12px; font-weight: 600; font-size: 0.8rem; }
    .chip-total { background: #e2e8f0; color: #334155; }
    .chip-success { background: #dcfce7; color: #166534; }
    .chip-error { background: #fee2e2; color: #991b1b; }
    .table-scroll { max-height: 240px; overflow-y: auto; }
    table { width: 100%; }
    .badge-success { color: #16a34a; font-weight: 600; }
    .badge-error { color: #dc2626; font-weight: 600; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReplicarRangoDialogComponent {
  protected readonly dialogRef = inject(MatDialogRef<ReplicarRangoDialogComponent>);
  protected readonly data: ReplicarRangoData = inject(MAT_DIALOG_DATA);
  private readonly horariosService = inject(HorariosService);

  protected readonly diasSemana = DIAS_SEMANA;
  protected readonly displayedColumns = ['fecha', 'estado', 'motivo'];

  protected operacion: 'replicar' | 'actualizar' | 'eliminar' = 'replicar';
  protected dia: string = DIAS_SEMANA[0];
  protected idAsignacion: number | null = this.data.asignaciones[0]?.idAsignacion ?? null;
  protected idhora: number | null = this.data.franjas[0]?.idhora ?? null;
  protected desde: string = this.data.lunesActual;
  protected hasta: string = this.data.lunesActual;

  protected readonly cargando = signal(false);
  protected readonly errorLocal = signal<string | null>(null);
  protected readonly resultado = signal<ResultadoRangoDto | null>(null);

  protected obtenerMensaje(codigo: string): string {
    return obtenerMensajeError(codigo);
  }

  protected validarFechas(): void {
    if (!this.desde || !this.hasta) {
      this.errorLocal.set(null);
      return;
    }
    const check = this.horariosService.validarTopeRango(this.desde, this.hasta);
    if (!check.valido) {
      this.errorLocal.set(check.error ?? 'Rango inválido');
    } else {
      this.errorLocal.set(null);
    }
  }

  protected formularioValido(): boolean {
    return (
      !!this.operacion &&
      !!this.dia &&
      !!this.idAsignacion &&
      !!this.idhora &&
      !!this.desde &&
      !!this.hasta &&
      !this.errorLocal()
    );
  }

  protected ejecutarOperacion(): void {
    if (!this.formularioValido()) return;

    this.cargando.set(true);
    this.errorLocal.set(null);

    const req: OperacionRangoDto = {
      idAsignacion: this.idAsignacion!,
      dia: this.dia,
      idhora: this.idhora!,
      desde: this.desde,
      hasta: this.hasta,
    };

    let obs$;
    if (this.operacion === 'replicar') {
      obs$ = this.horariosService.replicarRango(req);
    } else if (this.operacion === 'actualizar') {
      obs$ = this.horariosService.actualizarRango(req);
    } else {
      obs$ = this.horariosService.eliminarRango(req);
    }

    obs$.subscribe({
      next: (res) => {
        this.cargando.set(false);
        this.resultado.set(res);
      },
      error: (err) => {
        this.cargando.set(false);
        const code = err?.error?.codigo || err?.error?.code;
        this.errorLocal.set(obtenerMensajeError(code) || err.message || 'Error al ejecutar la operación.');
      },
    });
  }
}
