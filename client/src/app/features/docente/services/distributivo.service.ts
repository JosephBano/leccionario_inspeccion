import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import type { Observable } from 'rxjs';

import { environment } from '@env/environment';
import type { MiParaleloDto, AlumnoNominaDto, PeriodoPorNivelDto } from '@features/docente/models/distributivo.model';

@Injectable({ providedIn: 'root' })
export class DistributivoService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}`;

  /**
   * GET /api/mis-paralelos?idPeriodo=...
   * Para un docente: solo los suyos. Inspector: lista vacía (no aplica).
   */
  misParalelos(idPeriodo?: string): Observable<{ items: MiParaleloDto[] }> {
    let params = new HttpParams();
    if (idPeriodo) {
      params = params.set('idPeriodo', idPeriodo);
    }
    return this.http.get<{ items: MiParaleloDto[] }>(`${this.base}/mis-paralelos`, { params });
  }

  /**
   * GET /api/periodos/por-nivel
   * Lista de niveles (tipos de licencia) con el período más reciente de cada uno.
   * El docente solo ve los niveles en los que tiene distributivo.
   */
  periodosPorNivel(): Observable<{ items: PeriodoPorNivelDto[] }> {
    return this.http.get<{ items: PeriodoPorNivelDto[] }>(`${this.base}/periodos/por-nivel`);
  }

  /**
   * GET /api/paralelos/{idAsignacion}/alumnos
   * Nómina completa (incluye retirados=false y oyentes si aplica).
   */
  alumnos(idAsignacion: number): Observable<AlumnoNominaDto[]> {
    return this.http.get<AlumnoNominaDto[]>(`${this.base}/paralelos/${idAsignacion}/alumnos`);
  }
}
