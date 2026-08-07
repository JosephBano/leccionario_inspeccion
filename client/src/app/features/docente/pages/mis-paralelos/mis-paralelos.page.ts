import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { catchError, of, switchMap } from 'rxjs';

import { DistributivoService } from '@features/docente/services/distributivo.service';
import type { MiParaleloDto, PeriodoPorNivelDto } from '@features/docente/models/distributivo.model';
import { AuthService } from '@core/auth/services/auth.service';
import { Roles } from '@core/auth/models/roles';

interface ParaleloAgrupado {
  tipoLicencia: string;
  idNivel: number | null;
  periodos: ReadonlyArray<{ idPeriodo: string; paralelos: readonly MiParaleloDto[] }>;
}

@Component({
  selector: 'app-mis-paralelos-page',
  imports: [RouterLink, MatButtonModule, MatCardModule, MatChipsModule, MatIconModule],
  template: `
    <header class="page-header">
      <h1>Mis paralelos</h1>
      <p class="subtitle">
        Selecciona el paralelo donde vas a pasar lista hoy.
      </p>
    </header>

    @if (esInspector()) {
      <div class="info-card">
        <mat-icon aria-hidden="true">info</mat-icon>
        <span>
          Eres inspector. Los paralelos se consultan desde la sección de reportes.
        </span>
      </div>
    } @else if (cargando()) {
      <p class="loading">Cargando paralelos…</p>
    } @else if (error()) {
      <p class="error">No se pudieron cargar los paralelos. Intenta refrescar.</p>
    } @else if (grupos().length === 0) {
      <div class="empty">
        <mat-icon aria-hidden="true">inbox</mat-icon>
        <h2>Sin paralelos asignados</h2>
        <p>
          No tienes paralelos activos en este momento. Si crees que falta alguno,
          contacta al coordinador académico.
        </p>
      </div>
    } @else {
      <section class="grupos">
        @for (grupo of grupos(); track grupo.idNivel ?? grupo.tipoLicencia) {
          <article class="grupo">
            <h2 class="grupo-titulo">
              <mat-icon aria-hidden="true">school</mat-icon>
              {{ grupo.tipoLicencia }}
            </h2>

            @for (periodo of grupo.periodos; track periodo.idPeriodo) {
              <div class="periodo">
                <h3 class="periodo-titulo">{{ periodo.idPeriodo }}</h3>
                <ul class="paralelos">
                  @for (p of periodo.paralelos; track p.idAsignacion) {
                    <li>
                      <mat-card class="paralelo-card">
                        <mat-card-header>
                          <mat-card-title>{{ p.asignatura }}</mat-card-title>
                          <mat-card-subtitle>
                            Paralelo {{ p.paralelo }} · {{ p.jornada }} · {{ p.modalidad }}
                          </mat-card-subtitle>
                        </mat-card-header>
                        <mat-card-content>
                          <div class="meta">
                            <mat-chip-set>
                              <mat-chip>
                                {{ p.totalAlumnos }} alumnos
                              </mat-chip>
                              <mat-chip>
                                {{ p.sesionesRegistradas }} sesiones
                              </mat-chip>
                              @if (p.ultimaSesion) {
                                <mat-chip>Última: {{ p.ultimaSesion }}</mat-chip>
                              }
                            </mat-chip-set>
                          </div>
                          @if (p.fechaInicial && p.fechaFin) {
                            <p class="rango">
                              Vigencia: {{ p.fechaInicial }} → {{ p.fechaFin }}
                            </p>
                          }
                        </mat-card-content>
                        <mat-card-actions>
                          <a
                            mat-flat-button
                            color="primary"
                            [routerLink]="['/paralelos', p.idAsignacion, 'pasar-lista']"
                          >
                            <mat-icon>edit_note</mat-icon>
                            Pasar lista
                          </a>
                        </mat-card-actions>
                      </mat-card>
                    </li>
                  }
                </ul>
              </div>
            }
          </article>
        }
      </section>
    }
  `,
  styles: [`
    .page-header { margin-bottom: 16px; }
    .subtitle { color: var(--cplec-ink-muted); margin: 4px 0 0; }
    .loading { color: var(--cplec-ink-muted); }
    .error { color: var(--cplec-color-ausente, #c5211f); }
    .info-card, .empty {
      display: flex; align-items: center; gap: 12px;
      background: #fff; border: 1px solid #e3e7ef;
      border-top: 3px solid var(--cplec-gold);
      border-radius: var(--cplec-radius-md, 8px);
      padding: 16px;
    }
    .empty { flex-direction: column; text-align: center; padding: 32px; }
    .empty mat-icon { font-size: 40px; height: 40px; width: 40px; color: var(--cplec-navy-light); }
    .grupos { display: flex; flex-direction: column; gap: 24px; }
    .grupo-titulo {
      display: inline-flex; align-items: center; gap: 8px;
      margin: 0 0 12px; color: var(--cplec-navy);
    }
    .periodo { margin-bottom: 12px; }
    .periodo-titulo {
      margin: 0 0 8px; font-size: 0.85rem; text-transform: uppercase;
      font-family: var(--cplec-font-display); font-weight: 500;
      color: var(--cplec-ink-muted); letter-spacing: 0.6px;
    }
    .paralelos {
      list-style: none; padding: 0; margin: 0;
      display: grid; gap: 12px;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    }
    .paralelo-card { height: 100%; }
    .meta { margin: 8px 0; }
    .rango { font-size: 0.85rem; color: var(--cplec-ink-muted); margin: 4px 0 0; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MisParalelosPage {
  private readonly distributivo = inject(DistributivoService);
  protected readonly auth = inject(AuthService);

  protected readonly esInspector = computed(() => this.auth.tieneRol(Roles.inspector));

  private readonly periodosResource = toSignal(
    this.distributivo.periodosPorNivel().pipe(
      switchMap((res) => of(res.items)),
      catchError(() => of<PeriodoPorNivelDto[]>([])),
    ),
    { initialValue: [] as PeriodoPorNivelDto[] },
  );

  private readonly paralelosResource = toSignal(
    this.distributivo.misParalelos().pipe(
      switchMap((res) => of(res.items)),
      catchError(() => of<MiParaleloDto[]>([])),
    ),
    { initialValue: [] as MiParaleloDto[] },
  );

  protected readonly cargando = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly grupos = computed<ParaleloAgrupado[]>(() => {
    const paralelos = this.paralelosResource();
    const niveles = this.periodosResource();

    if (paralelos.length === 0) {
      return [];
    }

    // Agrupar paralelos por tipoLicencia → periodo → lista
    const porTipo = new Map<string, Map<string, MiParaleloDto[]>>();

    for (const p of paralelos) {
      const tipoKey = p.tipoLicencia;
      if (!porTipo.has(tipoKey)) {
        porTipo.set(tipoKey, new Map());
      }
      const porPeriodo = porTipo.get(tipoKey)!;
      if (!porPeriodo.has(p.idPeriodo)) {
        porPeriodo.set(p.idPeriodo, []);
      }
      porPeriodo.get(p.idPeriodo)!.push(p);
    }

    const grupos: ParaleloAgrupado[] = [];
    for (const [tipoLicencia, periodosMap] of porTipo.entries()) {
      const periodos = Array.from(periodosMap.entries()).map(([idPeriodo, paralelosList]) => ({
        idPeriodo,
        paralelos: paralelosList.sort((a, b) => a.paralelo.localeCompare(b.paralelo)),
      }));
      periodos.sort((a, b) => b.idPeriodo.localeCompare(a.idPeriodo));
      const idNivel = niveles.find((n) => n.tipoLicencia === tipoLicencia)?.idNivel ?? null;
      grupos.push({ tipoLicencia, idNivel, periodos });
    }

    return grupos.sort((a, b) => a.tipoLicencia.localeCompare(b.tipoLicencia));
  });
}
