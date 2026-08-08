import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  effect,
  inject,
  input,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';

import { PasarListaStore } from './pasar-lista.store';
import { EstadoAsistencia, TODOS_ESTADOS } from '@features/docente/models/asistencia.model';
import type { EstadoAsistenciaValue } from '@features/docente/models/asistencia.model';

interface EstadoBadge {
  readonly value: EstadoAsistenciaValue;
  readonly label: string;
  readonly icon: string;
  readonly color: string;
  readonly bg: string;
}

const ESTADOS_BADGE: readonly EstadoBadge[] = [
  { value: EstadoAsistencia.Presente, label: 'Presente', icon: 'check_circle', color: '#2e7d32', bg: '#e8f5e9' },
  { value: EstadoAsistencia.Ausente, label: 'Ausente', icon: 'cancel', color: '#c5211f', bg: '#fdecea' },
  { value: EstadoAsistencia.Atraso, label: 'Atraso', icon: 'schedule', color: '#ef6c00', bg: '#fff3e0' },
  { value: EstadoAsistencia.Justificado, label: 'Justificado', icon: 'verified', color: '#1565c0', bg: '#e3f2fd' },
] as const;

@Component({
  selector: 'app-pasar-lista-page',
  providers: [PasarListaStore],
  imports: [
    FormsModule,
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatDialogModule,
    MatIconModule,
    MatMenuModule,
  ],
  template: `
    <header class="page-header">
      <a mat-button routerLink="/mi-horario">
        <mat-icon>arrow_back</mat-icon>
        Mi horario
      </a>
      <h1>Pasar lista</h1>
      @if (store.sesion(); as s) {
        <p class="fecha">{{ s.fecha }}</p>
      }
    </header>

    @if (store.errorGuardar(); as e) {
      <div class="error-banner" role="alert" style="margin: 16px; padding: 16px; border-radius: 8px; background: #fdecea; color: #c5211f; display: flex; align-items: center; gap: 8px;">
        <mat-icon aria-hidden="true">error</mat-icon>
        <span>{{ e }}</span>
      </div>
    } @else if (!store.sesion()) {
      <p class="loading">Abriendo la sesión de esta clase…</p>
    } @else {
      <section class="resumen">
        <mat-chip-set>
          <mat-chip class="chip-presente">
            <mat-icon matChipAvatar>check_circle</mat-icon>
            {{ store.totalPresentes() }} presentes
          </mat-chip>
          <mat-chip class="chip-ausente">
            <mat-icon matChipAvatar>cancel</mat-icon>
            {{ store.totalAusentes() }} ausentes
          </mat-chip>
          <mat-chip class="chip-atraso">
            <mat-icon matChipAvatar>schedule</mat-icon>
            {{ store.totalAtrasos() }} atrasos
          </mat-chip>
          <mat-chip class="chip-justificado">
            <mat-icon matChipAvatar>verified</mat-icon>
            {{ store.totalJustificados() }} justificados
          </mat-chip>
          <mat-chip>{{ store.totalAlumnos() }} en total</mat-chip>
        </mat-chip-set>

        <div class="indicadores">
          @if (store.hayCambios()) {
            <span class="indicador pendiente">
              <mat-icon aria-hidden="true">edit</mat-icon>
              Cambios sin guardar
            </span>
          } @else if (store.ultimoGuardado(); as u) {
            <span class="indicador ok">
              <mat-icon aria-hidden="true">cloud_done</mat-icon>
              Guardado a las {{ u | date: 'HH:mm:ss' }}
            </span>
          }
        </div>

        <div class="acciones">
          <button mat-stroked-button (click)="marcarTodosPresentes()" [disabled]="!store.hayCambios()">
            <mat-icon>done_all</mat-icon>
            Todos presentes
          </button>
          <button
            mat-flat-button
            color="primary"
            (click)="guardar()"
            [disabled]="store.guardando() || !store.hayCambios()"
          >
            @if (store.guardando()) {
              Guardando…
            } @else {
              <ng-container>
                <mat-icon>save</mat-icon>
                Guardar lista
              </ng-container>
            }
          </button>
        </div>
      </section>

      <ul class="lista">
        @for (m of store.marcas(); track m.idMatricula) {
          <li>
            <button
              type="button"
              class="alumno touch-target"
              [class.estado-presente]="m.estado === 'presente'"
              [class.estado-ausente]="m.estado === 'ausente'"
              [class.estado-atraso]="m.estado === 'atraso'"
              [class.estado-justificado]="m.estado === 'justificado'"
              [attr.aria-label]="'Cambiar estado de ' + nombreCompleto(m) + ' (actual: ' + etiqueta(m.estado) + ')'"
              (click)="store.ciclarEstado(m.idMatricula)"
              [matMenuTriggerFor]="menu"
              [matMenuTriggerData]="{ idMatricula: m.idMatricula }"
            >
              <span class="estado-icon">
                <mat-icon aria-hidden="true">{{ icono(m.estado) }}</mat-icon>
              </span>
              <span class="alumno-info">
                <span class="alumno-nombre">{{ nombreCompleto(m) }}</span>
                @if (m.estado === 'atraso' && m.minutosAtraso) {
                  <span class="alumno-meta">{{ m.minutosAtraso }} min</span>
                }
              </span>
              <span class="estado-label">{{ etiqueta(m.estado) }}</span>
            </button>

            <mat-menu #menu="matMenu">
              @for (e of estados; track e) {
                <button mat-menu-item (click)="store.marcar(m.idMatricula, e)">
                  <mat-icon>{{ icono(e) }}</mat-icon>
                  <span>Marcar como {{ etiqueta(e) }}</span>
                </button>
              }
            </mat-menu>
          </li>
        }
      </ul>
    }
  `,
  styles: [`
    :host { display: block; }
    .page-header { margin-bottom: 16px; }
    .fecha { color: #5b6175; margin: 4px 0 0; }
    .loading { color: #5b6175; }
    .resumen {
      background: #fff;
      border: 1px solid #e3e7ef;
      border-radius: var(--cplec-radius-md, 8px);
      padding: 16px;
      margin-bottom: 16px;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
    .indicadores { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
    .indicador { display: inline-flex; align-items: center; gap: 6px; font-size: 0.9rem; }
    .indicador.ok { color: #2e7d32; }
    .indicador.pendiente { color: #ef6c00; }
    .error {
      display: flex; align-items: center; gap: 8px;
      background: var(--cplec-color-ausente-bg, #fdecea);
      color: var(--cplec-color-ausente, #c5211f);
      padding: 10px 12px; border-radius: 8px;
    }
    .acciones { display: flex; gap: 8px; flex-wrap: wrap; }
    .lista {
      list-style: none; padding: 0; margin: 0;
      display: grid; gap: 8px;
    }
    .alumno {
      display: grid;
      grid-template-columns: 48px 1fr auto;
      align-items: center;
      gap: 12px;
      width: 100%;
      padding: 12px 16px;
      border: 1px solid #e3e7ef;
      border-radius: var(--cplec-radius-md, 8px);
      background: #fff;
      cursor: pointer;
      transition: transform 80ms ease, box-shadow 80ms ease;
      text-align: left;
    }
    .alumno:hover { box-shadow: 0 2px 8px rgba(16, 42, 96, 0.08); }
    .alumno:active { transform: scale(0.99); }
    .estado-icon { display: inline-flex; align-items: center; justify-content: center; }
    .estado-icon mat-icon { font-size: 28px; height: 28px; width: 28px; }
    .alumno-nombre { font-weight: 500; }
    .alumno-meta { font-size: 0.85rem; color: #5b6175; }
    .estado-label {
      font-size: 0.78rem; font-weight: 600;
      padding: 4px 10px; border-radius: 999px;
      text-transform: uppercase; letter-spacing: 0.4px;
    }
    .estado-presente .estado-icon mat-icon { color: #2e7d32; }
    .estado-presente .estado-label { background: #e8f5e9; color: #2e7d32; }
    .estado-ausente .estado-icon mat-icon { color: #c5211f; }
    .estado-ausente .estado-label { background: #fdecea; color: #c5211f; }
    .estado-atraso .estado-icon mat-icon { color: #ef6c00; }
    .estado-atraso .estado-label { background: #fff3e0; color: #ef6c00; }
    .estado-justificado .estado-icon mat-icon { color: #1565c0; }
    .estado-justificado .estado-label { background: #e3f2fd; color: #1565c0; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PasarListaPage {
  protected readonly idAsignacion = input.required<number>();
  protected readonly idHorarioInicio = input<number | null>(null);
  protected readonly store = inject(PasarListaStore);
  private readonly snack = inject(MatSnackBar);
  private readonly router = inject(Router);

  protected readonly estados = TODOS_ESTADOS;

  constructor() {
    effect(() => {
      const id = this.idAsignacion();
      const bloque = this.idHorarioInicio();
      if (!id) return;

      // Sin bloque no hay asistencia: se vuelve a la agenda a elegirlo.
      if (
        bloque === null ||
        bloque === undefined ||
        String(bloque).trim() === '' ||
        Number.isNaN(Number(bloque)) ||
        Number(bloque) <= 0
      ) {
        this.snack.open('Elige el bloque de clase para pasar lista.', 'OK', { duration: 4000 });
        void this.router.navigate(['/paralelos', id, 'agenda']);
        return;
      }

      this.store.cargar(id, Number(bloque), 'Clase del día');
    });
  }

  protected nombreCompleto(m: { alumno: { apellidos: string; nombres: string } }): string {
    return `${m.alumno.apellidos} ${m.alumno.nombres}`.trim();
  }

  protected etiqueta(estado: EstadoAsistenciaValue): string {
    return ESTADOS_BADGE.find((e) => e.value === estado)?.label ?? estado;
  }

  protected icono(estado: EstadoAsistenciaValue): string {
    return ESTADOS_BADGE.find((e) => e.value === estado)?.icon ?? 'help_outline';
  }

  protected marcarTodosPresentes(): void {
    for (const m of this.store.marcas()) {
      if (m.estado !== EstadoAsistencia.Presente) {
        this.store.marcar(m.idMatricula, EstadoAsistencia.Presente);
      }
    }
  }

  protected async guardar(): Promise<void> {
    try {
      await this.store.guardar();
      this.snack.open('Lista guardada.', 'OK', { duration: 2500 });
    } catch {
      // El store ya tiene el mensaje en `errorGuardar`.
    }
  }

  @HostListener('window:beforeunload', ['$event'])
  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.store.hayCambios()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
}
