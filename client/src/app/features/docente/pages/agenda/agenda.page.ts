import type { OnInit} from '@angular/core';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { AgendaStore } from './agenda.store';
import type { BloqueAgendaDto } from '../../models/agenda.model';
import { HorarioGridComponent } from '@features/inspector/pages/horarios/components/horario-grid';

@Component({
  selector: 'app-agenda-page',
  standalone: true,
  providers: [AgendaStore],
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatProgressSpinnerModule,
    HorarioGridComponent,
  ],
  template: `
    <div class="page-container">
      <header class="page-header">
        <div>
          <button mat-button routerLink="/mis-paralelos" class="btn-back">
            <mat-icon>arrow_back</mat-icon>
            Volver a mis paralelos
          </button>
          <h1>Agenda de Clases del Paralelo</h1>
          <p class="subtitle">Seleccione un bloque de horario planificado para pasar lista.</p>
        </div>

        <div class="week-nav">
          <button mat-icon-button (click)="store.cambiarSemana(-1)" title="Semana anterior">
            <mat-icon>chevron_left</mat-icon>
          </button>
          <span class="week-label">Semana del {{ store.lunes() }}</span>
          <button mat-icon-button (click)="store.cambiarSemana(1)" title="Semana siguiente">
            <mat-icon>chevron_right</mat-icon>
          </button>
        </div>
      </header>

      @if (store.error()) {
        <div class="alert alert-error" role="alert">
          <mat-icon>error</mat-icon>
          <span>{{ store.error() }}</span>
        </div>
      }

      <section class="bloques-section">
        <h2>Bloques de la Semana</h2>

        @if (store.cargando()) {
          <div class="spinner-box">
            <mat-spinner diameter="36"></mat-spinner>
            <span>Cargando agenda...</span>
          </div>
        } @else if (store.bloques().length === 0) {
          <div class="empty-agenda">
            <mat-icon class="empty-icon">event_busy</mat-icon>
            <h3>Sin clases planificadas en esta semana</h3>
            <p>Este paralelo no tiene bloques de horario registrados para la semana del {{ store.lunes() }}.</p>
          </div>
        } @else {
          <div class="bloques-grid">
            @for (b of store.bloques(); track trackBloque(b)) {
              <mat-card
                class="bloque-card"
                [class.estado-pendiente]="b.estado === 'Pendiente'"
                [class.estado-borrador]="b.estado === 'Borrador'"
                [class.estado-cerrada]="b.estado === 'Cerrada'"
                [class.estado-futura]="b.estado === 'Futura'"
              >
                <mat-card-header>
                  <div class="bloque-header-content">
                    <span class="bloque-dia">{{ b.dia }} {{ b.fecha }}</span>
                    <span class="bloque-time">{{ b.horaInicio }} – {{ b.horaFin }}</span>
                  </div>
                </mat-card-header>

                <mat-card-content class="bloque-body">
                  <div class="bloque-info">
                    <span class="info-label">Bloque #{{ b.numeroBloque }}</span>
                    <span class="info-minutos">{{ b.minutosPlanificados }} min</span>
                  </div>

                  <div class="status-chip-row">
                    @switch (b.estado) {
                      @case ('Pendiente') {
                        <span class="chip chip-pendiente">
                          <mat-icon class="chip-icon">schedule</mat-icon> Pendiente
                        </span>
                      }
                      @case ('Borrador') {
                        <span class="chip chip-borrador">
                          <mat-icon class="chip-icon">edit_note</mat-icon> Borrador sin cerrar
                        </span>
                      }
                      @case ('Cerrada') {
                        <span class="chip chip-cerrada">
                          <mat-icon class="chip-icon">check_circle</mat-icon> Cerrada
                        </span>
                        @if (b.diasRetraso > 0) {
                          <span class="badge-retraso">{{ b.diasRetraso }}d retraso</span>
                        }
                      }
                      @case ('Futura') {
                        <span class="chip chip-futura">
                          <mat-icon class="chip-icon">lock_clock</mat-icon> Clases futuras
                        </span>
                      }
                    }
                  </div>
                </mat-card-content>

                <mat-card-actions align="end">
                  @if (b.estado === 'Pendiente' || b.estado === 'Borrador') {
                    <button
                      mat-raised-button
                      color="primary"
                      (click)="pasarLista(b)"
                    >
                      <mat-icon>how_to_reg</mat-icon>
                      Pasar Lista
                    </button>
                  } @else if (b.estado === 'Cerrada') {
                    <button
                      mat-stroked-button
                      (click)="pasarLista(b)"
                    >
                      <mat-icon>visibility</mat-icon>
                      Consultar
                    </button>
                  } @else {
                    <button mat-button disabled>
                      <mat-icon>block</mat-icon>
                      No Disponible
                    </button>
                  }
                </mat-card-actions>
              </mat-card>
            }
          </div>
        }
      </section>

      @if (store.grid(); as g) {
        <section class="grid-section">
          <h2>Cuadrante Horario del Paralelo (Lectura)</h2>
          <app-horario-grid
            [franjas]="g.franjas"
            [dias]="g.dias"
            [celdas]="g.celdas"
            modo="lectura"
          />
        </section>
      }
    </div>
  `,
  styles: [`
    .page-container { display: flex; flex-direction: column; gap: 20px; }
    .page-header { display: flex; justify-content: space-between; align-items: flex-start; }
    .btn-back { margin-bottom: 8px; font-weight: 500; color: #475569; }
    .page-header h1 { margin: 0; font-size: 1.5rem; color: #0f172a; }
    .subtitle { margin: 4px 0 0; color: #64748b; font-size: 0.9rem; }
    .week-nav { display: flex; align-items: center; gap: 8px; background: #fff; padding: 4px 8px; border-radius: 6px; border: 1px solid #cbd5e1; }
    .week-label { font-weight: 600; font-size: 0.9rem; min-width: 170px; text-align: center; }
    .alert-error { display: flex; align-items: center; gap: 8px; background: #fef2f2; border: 1px solid #fca5a5; color: #991b1b; padding: 12px; border-radius: 6px; }
    .bloques-section h2, .grid-section h2 { margin: 0 0 12px; font-size: 1.1rem; color: #1e293b; }
    .spinner-box { display: flex; flex-direction: column; align-items: center; padding: 40px; gap: 12px; color: #64748b; }
    .empty-agenda { text-align: center; padding: 40px 20px; background: #fff; border: 1px dashed #cbd5e1; border-radius: 8px; color: #64748b; }
    .empty-icon { font-size: 48px; width: 48px; height: 48px; color: #94a3b8; }
    .bloques-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px; }
    .bloque-card { transition: transform 0.15s ease, box-shadow 0.15s ease; border-left: 4px solid #cbd5e1; }
    .bloque-card:hover { transform: translateY(-2px); box-shadow: 0 6px 16px rgba(0,0,0,0.08); }
    .bloque-card.estado-pendiente { border-left-color: #3b82f6; }
    .bloque-card.estado-borrador { border-left-color: #f59e0b; background: #fffdf5; }
    .bloque-card.estado-cerrada { border-left-color: #10b981; opacity: 0.9; }
    .bloque-card.estado-futura { border-left-color: #94a3b8; opacity: 0.7; }
    .bloque-header-content { display: flex; flex-direction: column; }
    .bloque-dia { font-weight: 700; color: #0f172a; text-transform: capitalize; }
    .bloque-time { font-size: 0.85rem; color: #3b82f6; font-weight: 600; }
    .bloque-body { display: flex; flex-direction: column; gap: 8px; margin-top: 8px; }
    .bloque-info { display: flex; justify-content: space-between; font-size: 0.8rem; color: #64748b; }
    .status-chip-row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .chip { display: inline-flex; align-items: center; gap: 4px; padding: 4px 8px; border-radius: 12px; font-size: 0.75rem; font-weight: 600; }
    .chip-icon { font-size: 14px; width: 14px; height: 14px; }
    .chip-pendiente { background: #dbeafe; color: #1e40af; }
    .chip-borrador { background: #fef3c7; color: #92400e; }
    .chip-cerrada { background: #d1fae5; color: #065f46; }
    .chip-futura { background: #f1f5f9; color: #475569; }
    .badge-retraso { background: #fee2e2; color: #991b1b; padding: 2px 6px; border-radius: 4px; font-size: 0.7rem; font-weight: 600; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AgendaPage implements OnInit {
  protected readonly store = inject(AgendaStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  ngOnInit(): void {
    const idAsig = Number(this.route.snapshot.paramMap.get('idAsignacion'));
    const qp = this.route.snapshot.queryParams;
    this.store.inicializar(idAsig, null, qp['lunes']);
  }

  protected trackBloque(b: BloqueAgendaDto): string {
    return `${b.fecha}_${b.numeroBloque}_${b.idHorarioInicio}`;
  }

  protected pasarLista(b: BloqueAgendaDto): void {
    const idAsig = this.store.idAsignacion();
    if (!idAsig) return;

    this.router.navigate(['/paralelos', idAsig, 'pasar-lista'], {
      queryParams: {
        idHorarioInicio: b.idHorarioInicio,
        fecha: b.fecha,
      },
    });
  }
}
