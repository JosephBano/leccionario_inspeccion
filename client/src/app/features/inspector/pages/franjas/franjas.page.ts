import type { OnInit} from '@angular/core';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';

import { FranjasService } from '../../services/franjas.service';
import type { FranjaDto, CrearFranjaDto} from '../../models/horario.model';
import { obtenerMensajeError } from '../../models/horario.model';

@Component({
  selector: 'app-franjas-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatCardModule,
  ],
  template: `
    <div class="page-container">
      <header class="page-header">
        <div>
          <h1>Catálogo de Franjas Horarias</h1>
          <p class="subtitle">Administración de franjas horarias (Franjas Z)</p>
        </div>

        <button mat-raised-button color="primary" (click)="abrirFormularioNuevo()">
          <mat-icon>add</mat-icon>
          Nueva Franja
        </button>
      </header>

      @if (errorGlobal()) {
        <div class="alert alert-danger" role="alert">
          <mat-icon>error</mat-icon>
          <span>{{ errorGlobal() }}</span>
        </div>
      }

      @if (mostrandoFormulario()) {
        <mat-card class="form-card">
          <mat-card-header>
            <mat-card-title>
              {{ franjaEditando() ? 'Editar Franja' : 'Crear Franja Z' }}
            </mat-card-title>
          </mat-card-header>

          <mat-card-content class="form-body">
            <div class="field-row">
              <mat-form-field appearance="outline">
                <mat-label>Hora Inicio (HH:mm)</mat-label>
                <input matInput type="time" [(ngModel)]="horaInicio" (change)="validarHoras()" />
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Hora Fin (HH:mm)</mat-label>
                <input matInput type="time" [(ngModel)]="horaFin" (change)="validarHoras()" />
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Número de Hora (Opcional)</mat-label>
                <input matInput type="number" [(ngModel)]="numeroHora" />
              </mat-form-field>
            </div>

            @if (errorForm()) {
              <div class="alert alert-danger" role="alert">
                <mat-icon>warning</mat-icon>
                <span>{{ errorForm() }}</span>
              </div>
            }
          </mat-card-content>

          <mat-card-actions align="end">
            <button mat-button (click)="cerrarFormulario()" [disabled]="guardando()">Cancelar</button>
            <button
              mat-raised-button
              color="primary"
              [disabled]="!formularioValido() || guardando()"
              (click)="guardarFranja()"
            >
              @if (guardando()) {
                <mat-spinner diameter="18"></mat-spinner>
              } @else {
                Guardar
              }
            </button>
          </mat-card-actions>
        </mat-card>
      }

      <div class="table-container mat-elevation-z2">
        @if (cargando()) {
          <div class="spinner-box">
            <mat-spinner diameter="36"></mat-spinner>
            <span>Cargando catálogo...</span>
          </div>
        } @else {
          <table mat-table [dataSource]="franjas()" class="full-width">
            <ng-container matColumnDef="numeroHora">
              <th mat-header-cell *matHeaderCellDef># Bloque</th>
              <td mat-cell *matCellDef="let f">{{ f.numeroHora ?? '-' }}</td>
            </ng-container>

            <ng-container matColumnDef="horaInicio">
              <th mat-header-cell *matHeaderCellDef>Hora Inicio</th>
              <td mat-cell *matCellDef="let f">
                <strong>{{ f.horaInicio }}</strong>
              </td>
            </ng-container>

            <ng-container matColumnDef="horaFin">
              <th mat-header-cell *matHeaderCellDef>Hora Fin</th>
              <td mat-cell *matCellDef="let f">
                <strong>{{ f.horaFin }}</strong>
              </td>
            </ng-container>

            <ng-container matColumnDef="tipo">
              <th mat-header-cell *matHeaderCellDef>Tipo</th>
              <td mat-cell *matCellDef="let f">
                @if (f.tipo === 'X') {
                  <span class="badge badge-inst">Institucional</span>
                } @else {
                  <span class="badge badge-normal">Normal (Z)</span>
                }
              </td>
            </ng-container>

            <ng-container matColumnDef="acciones">
              <th mat-header-cell *matHeaderCellDef class="text-right">Acciones</th>
              <td mat-cell *matCellDef="let f" class="text-right">
                @if (f.tipo !== 'X') {
                  <button mat-icon-button color="primary" (click)="editarFranja(f)" title="Editar">
                    <mat-icon>edit</mat-icon>
                  </button>

                  <button mat-icon-button color="warn" (click)="desactivarFranja(f)" title="Desactivar">
                    <mat-icon>delete</mat-icon>
                  </button>
                } @else {
                  <span class="text-muted" title="Las franjas de instituto no son editables">Protegida</span>
                }
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
          </table>
        }
      </div>
    </div>
  `,
  styles: [`
    .page-container { display: flex; flex-direction: column; gap: 16px; }
    .page-header { display: flex; justify-content: space-between; align-items: center; }
    .page-header h1 { margin: 0; font-size: 1.5rem; color: #0f172a; }
    .subtitle { margin: 4px 0 0; color: #64748b; font-size: 0.9rem; }
    .alert { display: flex; align-items: center; gap: 8px; padding: 12px; border-radius: 6px; }
    .alert-danger { background: #fef2f2; color: #991b1b; border: 1px solid #fca5a5; }
    .form-card { margin-bottom: 8px; }
    .field-row { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 12px; margin-top: 12px; }
    .table-container { background: #fff; border-radius: 8px; overflow: hidden; }
    .full-width { width: 100%; }
    .text-right { text-align: right; }
    .text-muted { font-size: 0.8rem; color: #94a3b8; }
    .spinner-box { display: flex; flex-direction: column; align-items: center; padding: 40px; gap: 12px; color: #64748b; }
    .badge { padding: 3px 8px; border-radius: 12px; font-size: 0.75rem; font-weight: 600; }
    .badge-inst { background: #dbeafe; color: #1e40af; }
    .badge-normal { background: #f1f5f9; color: #334155; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FranjasPage implements OnInit {
  private readonly franjasService = inject(FranjasService);

  protected readonly franjas = signal<FranjaDto[]>([]);
  protected readonly cargando = signal<boolean>(true);
  protected readonly errorGlobal = signal<string | null>(null);

  protected readonly mostrandoFormulario = signal<boolean>(false);
  protected readonly franjaEditando = signal<FranjaDto | null>(null);
  protected readonly guardando = signal<boolean>(false);
  protected readonly errorForm = signal<string | null>(null);

  protected horaInicio = '';
  protected horaFin = '';
  protected numeroHora: number | null = null;

  protected readonly displayedColumns = ['numeroHora', 'horaInicio', 'horaFin', 'tipo', 'acciones'];

  ngOnInit(): void {
    this.cargarFranjas();
  }

  cargarFranjas(): void {
    this.cargando.set(true);
    this.errorGlobal.set(null);
    this.franjasService.listar().subscribe({
      next: (list) => {
        const sorted = [...list].sort((a, b) => a.horaInicio.localeCompare(b.horaInicio));
        this.franjas.set(sorted);
        this.cargando.set(false);
      },
      error: (err) => {
        this.cargando.set(false);
        const code = err?.error?.codigo || err?.error?.code;
        this.errorGlobal.set(obtenerMensajeError(code));
      },
    });
  }

  abrirFormularioNuevo(): void {
    this.franjaEditando.set(null);
    this.horaInicio = '';
    this.horaFin = '';
    this.numeroHora = null;
    this.errorForm.set(null);
    this.mostrandoFormulario.set(true);
  }

  editarFranja(f: FranjaDto): void {
    this.franjaEditando.set(f);
    this.horaInicio = f.horaInicio;
    this.horaFin = f.horaFin;
    this.numeroHora = f.numeroHora ?? null;
    this.errorForm.set(null);
    this.mostrandoFormulario.set(true);
  }

  cerrarFormulario(): void {
    this.mostrandoFormulario.set(false);
    this.franjaEditando.set(null);
  }

  validarHoras(): void {
    if (!this.horaInicio || !this.horaFin) {
      this.errorForm.set(null);
      return;
    }
    if (this.horaInicio >= this.horaFin) {
      this.errorForm.set('La hora de inicio debe ser estrictamente menor que la hora de fin.');
    } else {
      this.errorForm.set(null);
    }
  }

  formularioValido(): boolean {
    return !!this.horaInicio && !!this.horaFin && this.horaInicio < this.horaFin && !this.errorForm();
  }

  guardarFranja(): void {
    if (!this.formularioValido()) return;

    this.guardando.set(true);
    this.errorForm.set(null);

    const dto: CrearFranjaDto = {
      horaInicio: this.horaInicio,
      horaFin: this.horaFin,
      numeroHora: this.numeroHora,
    };

    const ed = this.franjaEditando();
    const obs$ = ed
      ? this.franjasService.actualizar(ed.idhora, dto)
      : this.franjasService.crear(dto);

    obs$.subscribe({
      next: () => {
        this.guardando.set(false);
        this.cerrarFormulario();
        this.cargarFranjas();
      },
      error: (err) => {
        this.guardando.set(false);
        const code = err?.error?.codigo || err?.error?.code;
        this.errorForm.set(obtenerMensajeError(code));
      },
    });
  }

  desactivarFranja(f: FranjaDto): void {
    if (!confirm(`¿Está seguro de desactivar la franja ${f.horaInicio} – ${f.horaFin}?`)) {
      return;
    }

    this.franjasService.desactivar(f.idhora).subscribe({
      next: () => {
        this.cargarFranjas();
      },
      error: (err) => {
        const code = err?.error?.codigo || err?.error?.code;
        this.errorGlobal.set(obtenerMensajeError(code));
      },
    });
  }
}
