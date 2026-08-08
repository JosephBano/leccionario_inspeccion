import type { OnInit} from '@angular/core';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { ReportesService } from '../../services/reportes.service';
import type { SesionTardiaDto, DiaSinRegistrarDto} from '../../models/horario.model';
import { obtenerMensajeError } from '../../models/horario.model';

function getFechaHaceDias(dias: number): string {
  const d = new Date();
  d.setDate(d.getDate() - dias);
  return d.toISOString().split('T')[0];
}

function getFechaHoy(): string {
  return new Date().toISOString().split('T')[0];
}

@Component({
  selector: 'app-reportes-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatTabsModule,
    MatTableModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <div class="page-container">
      <header class="page-header">
        <div>
          <h1>Reportes de Inspección y Asistencia</h1>
          <p class="subtitle">Monitoreo de cumplimiento de sesiones tardías y días sin registro</p>
        </div>
      </header>

      <section class="filter-section">
        <div class="filter-inputs">
          <mat-form-field appearance="outline">
            <mat-label>Desde</mat-label>
            <input matInput type="date" [(ngModel)]="desde" (change)="actualizarParametros()" />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Hasta</mat-label>
            <input matInput type="date" [(ngModel)]="hasta" (change)="actualizarParametros()" />
          </mat-form-field>

          <button mat-raised-button color="primary" (click)="cargarReportes()">
            <mat-icon>search</mat-icon>
            Consultar
          </button>
        </div>
      </section>

      @if (error()) {
        <div class="alert alert-danger" role="alert">
          <mat-icon>error</mat-icon>
          <span>{{ error() }}</span>
        </div>
      }

      <mat-tab-group animationDuration="150ms" class="tabs-container">
        <!-- Pestaña 1: Sesiones Tardías -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon class="tab-icon">alarm_off</mat-icon>
            Sesiones Tardías ({{ tardias().length }})
          </ng-template>

          <div class="tab-body">
            @if (cargando()) {
              <div class="spinner-box">
                <mat-spinner diameter="36"></mat-spinner>
                <span>Cargando sesiones tardías...</span>
              </div>
            } @else if (tardias().length === 0) {
              <div class="empty-state">
                <mat-icon class="empty-icon">check_circle_outline</mat-icon>
                <h3>Sin sesiones tardías</h3>
                <p>No se registraron sesiones cerradas con atraso en el rango seleccionado.</p>
              </div>
            } @else {
              <table mat-table [dataSource]="tardias()" class="mat-elevation-z1 full-width">
                <ng-container matColumnDef="idSesion">
                  <th mat-header-cell *matHeaderCellDef># Sesión</th>
                  <td mat-cell *matCellDef="let row">{{ row.idSesion }}</td>
                </ng-container>

                <ng-container matColumnDef="idAsignacion">
                  <th mat-header-cell *matHeaderCellDef># Asignación</th>
                  <td mat-cell *matCellDef="let row">{{ row.idAsignacion }}</td>
                </ng-container>

                <ng-container matColumnDef="fecha">
                  <th mat-header-cell *matHeaderCellDef>Fecha</th>
                  <td mat-cell *matCellDef="let row">{{ row.fecha }}</td>
                </ng-container>

                <ng-container matColumnDef="docente">
                  <th mat-header-cell *matHeaderCellDef>Docente</th>
                  <td mat-cell *matCellDef="let row">
                    <strong>{{ row.nombreDocente || 'Sin docente' }}</strong>
                  </td>
                </ng-container>

                <ng-container matColumnDef="tema">
                  <th mat-header-cell *matHeaderCellDef>Tema</th>
                  <td mat-cell *matCellDef="let row">{{ row.tema }}</td>
                </ng-container>

                <ng-container matColumnDef="diasRetraso">
                  <th mat-header-cell *matHeaderCellDef class="text-right">Días de Retraso</th>
                  <td mat-cell *matCellDef="let row" class="text-right">
                    <span class="badge badge-error">{{ row.diasRetraso }} d</span>
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="columnsTardias"></tr>
                <tr mat-row *matRowDef="let row; columns: columnsTardias;"></tr>
              </table>
            }
          </div>
        </mat-tab>

        <!-- Pestaña 2: Días Sin Registrar -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon class="tab-icon">event_busy</mat-icon>
            Días sin Registrar ({{ sinRegistrar().length }})
          </ng-template>

          <div class="tab-body">
            @if (cargando()) {
              <div class="spinner-box">
                <mat-spinner diameter="36"></mat-spinner>
                <span>Cargando días sin registrar...</span>
              </div>
            } @else if (sinRegistrar().length === 0) {
              <div class="empty-state">
                <mat-icon class="empty-icon">verified</mat-icon>
                <h3>Al día con las asistencias</h3>
                <p>No existen días pasados con horario pendiente de sesión en el rango seleccionado.</p>
              </div>
            } @else {
              <table mat-table [dataSource]="sinRegistrar()" class="mat-elevation-z1 full-width">
                <ng-container matColumnDef="idAsignacion">
                  <th mat-header-cell *matHeaderCellDef># Asignación</th>
                  <td mat-cell *matCellDef="let row">{{ row.idAsignacion }}</td>
                </ng-container>

                <ng-container matColumnDef="fecha">
                  <th mat-header-cell *matHeaderCellDef>Fecha Pendiente</th>
                  <td mat-cell *matCellDef="let row">{{ row.fecha }}</td>
                </ng-container>

                <ng-container matColumnDef="docente">
                  <th mat-header-cell *matHeaderCellDef>Docente Responsable</th>
                  <td mat-cell *matCellDef="let row">
                    <strong>{{ row.nombreDocente || 'Sin docente' }}</strong>
                  </td>
                </ng-container>

                <ng-container matColumnDef="numeroBloque">
                  <th mat-header-cell *matHeaderCellDef>Bloque #</th>
                  <td mat-cell *matCellDef="let row">Bloque {{ row.numeroBloque }}</td>
                </ng-container>

                <ng-container matColumnDef="diasVencido">
                  <th mat-header-cell *matHeaderCellDef class="text-right">Días Vencido</th>
                  <td mat-cell *matCellDef="let row" class="text-right">
                    <span class="badge badge-warn">{{ row.diasVencido }} d</span>
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="columnsSinRegistrar"></tr>
                <tr mat-row *matRowDef="let row; columns: columnsSinRegistrar;"></tr>
              </table>
            }
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .page-container { display: flex; flex-direction: column; gap: 16px; }
    .page-header h1 { margin: 0; font-size: 1.5rem; color: #0f172a; }
    .subtitle { margin: 4px 0 0; color: #64748b; font-size: 0.9rem; }
    .filter-section {
      background: #f8fafc;
      padding: 12px 16px;
      border-radius: 8px;
      border: 1px solid #e2e8f0;
    }
    .filter-inputs { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
    mat-form-field { margin-bottom: -1.25em; }
    .alert-danger {
      display: flex; align-items: center; gap: 8px;
      padding: 12px; border-radius: 6px;
      background: #fef2f2; border: 1px solid #fca5a5; color: #991b1b;
    }
    .tab-icon { margin-right: 6px; }
    .tab-body { padding: 16px 0; }
    .full-width { width: 100%; }
    .text-right { text-align: right; }
    .spinner-box { display: flex; flex-direction: column; align-items: center; padding: 40px; gap: 12px; color: #64748b; }
    .empty-state { text-align: center; padding: 40px 20px; background: #fff; border: 1px dashed #cbd5e1; border-radius: 8px; color: #64748b; }
    .empty-icon { font-size: 44px; width: 44px; height: 44px; color: #94a3b8; }
    .badge { padding: 3px 8px; border-radius: 12px; font-size: 0.75rem; font-weight: 600; }
    .badge-error { background: #fee2e2; color: #991b1b; }
    .badge-warn { background: #fef3c7; color: #92400e; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportesPage implements OnInit {
  private readonly reportesService = inject(ReportesService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected desde: string = getFechaHaceDias(30);
  protected hasta: string = getFechaHoy();

  protected readonly tardias = signal<SesionTardiaDto[]>([]);
  protected readonly sinRegistrar = signal<DiaSinRegistrarDto[]>([]);
  protected readonly cargando = signal<boolean>(false);
  protected readonly error = signal<string | null>(null);

  protected readonly columnsTardias = ['idSesion', 'idAsignacion', 'fecha', 'docente', 'tema', 'diasRetraso'];
  protected readonly columnsSinRegistrar = ['idAsignacion', 'fecha', 'docente', 'numeroBloque', 'diasVencido'];

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParams;
    if (qp['desde']) this.desde = qp['desde'];
    if (qp['hasta']) this.hasta = qp['hasta'];
    this.cargarReportes();
  }

  protected actualizarParametros(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { desde: this.desde, hasta: this.hasta },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected cargarReportes(): void {
    this.actualizarParametros();
    this.cargando.set(true);
    this.error.set(null);

    this.reportesService.obtenerSesionesTardias(this.desde, this.hasta).subscribe({
      next: (data) => {
        this.tardias.set(data);
      },
      error: (err) => {
        const code = err?.error?.codigo || err?.error?.code;
        this.error.set(obtenerMensajeError(code));
      },
    });

    this.reportesService.obtenerDiasSinRegistrar(this.desde, this.hasta).subscribe({
      next: (data) => {
        this.sinRegistrar.set(data);
        this.cargando.set(false);
      },
      error: (err) => {
        this.cargando.set(false);
        const code = err?.error?.codigo || err?.error?.code;
        this.error.set(obtenerMensajeError(code));
      },
    });
  }
}
