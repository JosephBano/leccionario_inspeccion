import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { HorariosStore } from './horarios.store';
import type { CeldaClickEvent } from './components/horario-grid';
import { HorarioGridComponent } from './components/horario-grid';
import { AsignacionPanelComponent } from './components/asignacion-panel';
import { ReplicarRangoDialogComponent } from './components/replicar-rango-dialog';
import type { ParaleloClave } from '@shared/paralelo-selector/paralelo-selector';
import { ParaleloSelector } from '@shared/paralelo-selector/paralelo-selector';

@Component({
  selector: 'app-horarios-page',
  standalone: true,
  providers: [HorariosStore],
  imports: [
    CommonModule,
    MatButtonModule,
    MatIconModule,
    MatDialogModule,
    MatProgressSpinnerModule,
    HorarioGridComponent,
    AsignacionPanelComponent,
    ParaleloSelector,
  ],
  template: `
    <div class="page-container">
      <header class="page-header">
        <div class="title-area">
          <h1>Gestión de Horarios</h1>
          <p class="subtitle">Armado y administración del grid semanal por paralelo</p>
        </div>

        <div class="header-actions">
          <button
            mat-raised-button
            color="primary"
            [disabled]="!store.clave() || !store.grid()"
            (click)="abrirDialogoRango()"
          >
            <mat-icon>date_range</mat-icon>
            Operaciones por Rango
          </button>
        </div>
      </header>

      <section class="toolbar-section">
        <app-paralelo-selector
          [periodos]="store.periodos()"
          [opciones]="store.paralelos()"
          [seleccion]="store.clave()"
          (periodoChange)="store.cargarParalelos($event)"
          (seleccionChange)="onParaleloChange($event)"
        />

        <div class="week-nav">
          <button mat-icon-button (click)="store.cambiarSemana(-1)" title="Semana anterior">
            <mat-icon>chevron_left</mat-icon>
          </button>

          <span class="week-label">
            Semana del {{ store.lunes() }}
          </span>

          <button mat-icon-button (click)="store.cambiarSemana(1)" title="Semana siguiente">
            <mat-icon>chevron_right</mat-icon>
          </button>
        </div>
      </section>

      @if (store.error()) {
        <div class="alert alert-error" role="alert">
          <mat-icon>error</mat-icon>
          <span>{{ store.error() }}</span>
        </div>
      }

      <div class="main-content" [class.with-panel]="store.panelAbierto()">
        <div class="grid-container">
          @if (store.cargando()) {
            <div class="loading-overlay">
              <mat-spinner diameter="40"></mat-spinner>
              <span>Cargando grid de horario...</span>
            </div>
          } @else if (store.grid(); as g) {
            <app-horario-grid
              [franjas]="g.franjas"
              [dias]="g.dias"
              [celdas]="g.celdas"
              modo="edicion"
              (celdaClick)="onCeldaClick($event)"
            />
          } @else {
            <div class="empty-state">
              <mat-icon class="empty-icon">calendar_month</mat-icon>
              <h3>Seleccione un paralelo</h3>
              <p>Elija un paralelo para visualizar y gestionar su cuadrante semanal.</p>
            </div>
          }
        </div>

        @if (store.panelAbierto()) {
          <aside class="panel-sidebar">
            <app-asignacion-panel
              [asignaciones]="store.asignaciones()"
              [celdaExistente]="store.celdaSeleccionada()?.celda || null"
              [dia]="store.celdaSeleccionada()?.dia || null"
              [franja]="store.celdaSeleccionada()?.franja || null"
              [resultadoConflicto]="store.resultadoConflicto()"
              [cargandoConflicto]="store.cargandoConflicto()"
              [cargandoGuardar]="store.cargandoGuardar()"
              (asignacionChange)="store.validarConflicto($event)"
              (guardar)="onGuardarCelda($event)"
              (eliminar)="store.eliminarCelda($event)"
              (cerrar)="store.cerrarPanel()"
            />
          </aside>
        }
      </div>
    </div>
  `,
  styles: [`
    .page-container {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }
    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .title-area h1 {
      margin: 0;
      font-size: 1.5rem;
      color: #0f172a;
    }
    .subtitle {
      margin: 4px 0 0;
      color: #64748b;
      font-size: 0.9rem;
    }
    .toolbar-section {
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 16px;
      background: #f8fafc;
      padding: 12px 16px;
      border-radius: 8px;
      border: 1px solid #e2e8f0;
    }
    .week-nav {
      display: flex;
      align-items: center;
      gap: 8px;
      background: #fff;
      padding: 4px 8px;
      border-radius: 6px;
      border: 1px solid #cbd5e1;
    }
    .week-label {
      font-weight: 600;
      font-size: 0.9rem;
      min-width: 170px;
      text-align: center;
    }
    .alert-error {
      display: flex;
      align-items: center;
      gap: 8px;
      background: #fef2f2;
      border: 1px solid #fca5a5;
      color: #991b1b;
      padding: 12px;
      border-radius: 6px;
    }
    .main-content {
      display: grid;
      grid-template-columns: 1fr;
      gap: 16px;
      transition: grid-template-columns 0.2s ease;
    }
    .main-content.with-panel {
      grid-template-columns: 1fr 340px;
    }
    .grid-container {
      position: relative;
      min-height: 400px;
    }
    .loading-overlay {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 12px;
      padding: 60px 0;
      color: #64748b;
    }
    .empty-state {
      text-align: center;
      padding: 60px 20px;
      background: #fff;
      border: 1px dashed #cbd5e1;
      border-radius: 8px;
      color: #64748b;
    }
    .empty-icon {
      font-size: 48px;
      width: 48px;
      height: 48px;
      color: #94a3b8;
    }
    .panel-sidebar {
      height: fit-content;
      position: sticky;
      top: 80px;
    }
    @media (max-width: 900px) {
      .main-content.with-panel {
        grid-template-columns: 1fr;
      }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HorariosPage {
  protected readonly store = inject(HorariosStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);

  constructor() {
    // Read initial query params
    const qp = this.route.snapshot.queryParams;
    this.store.inicializar({
      idPeriodo: qp['idPeriodo'],
      idNivel: qp['idNivel'],
      idSeccion: qp['idSeccion'],
      idModalidad: qp['idModalidad'],
      paralelo: qp['paralelo'],
      lunes: qp['lunes'],
    });

    // Keep URL synchronized with store state
    effect(() => {
      const clave = this.store.clave();
      const lunes = this.store.lunes();
      if (clave) {
        this.router.navigate([], {
          relativeTo: this.route,
          queryParams: {
            idPeriodo: clave.idPeriodo,
            idNivel: clave.idNivel,
            idSeccion: clave.idSeccion,
            idModalidad: clave.idModalidad,
            paralelo: clave.paralelo,
            lunes,
          },
          queryParamsHandling: 'merge',
          replaceUrl: true,
        });
      }
    });
  }

  protected onParaleloChange(clave: ParaleloClave): void {
    this.store.seleccionarParalelo(clave);
  }

  protected onCeldaClick(ev: CeldaClickEvent): void {
    this.store.abrirPanelCelda(ev.dia, ev.franja, ev.celda);
  }

  protected onGuardarCelda(ev: { idAsignacion: number; confirmarAdvertencias: boolean }): void {
    this.store.guardarCelda(ev.idAsignacion, ev.confirmarAdvertencias);
  }

  protected abrirDialogoRango(): void {
    const clave = this.store.clave();
    const grid = this.store.grid();
    if (!clave || !grid) return;

    const dialogRef = this.dialog.open(ReplicarRangoDialogComponent, {
      width: '560px',
      data: {
        clave,
        asignaciones: this.store.asignaciones(),
        franjas: grid.franjas,
        lunesActual: this.store.lunes(),
      },
    });

    dialogRef.afterClosed().subscribe((refrescar) => {
      if (refrescar) {
        this.store.cargarGrid();
      }
    });
  }
}
