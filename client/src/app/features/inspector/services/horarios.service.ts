import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import type { Observable} from 'rxjs';
import { throwError } from 'rxjs';
import { environment } from '@env/environment';
import type { ParaleloClave } from '@shared/paralelo-selector/paralelo-selector';
import type {
  GridDto,
  SolicitudConflictoDto,
  ResultadoConflictoDto,
  CrearCeldaDto,
  CeldaCreadaDto,
  OperacionRangoDto,
  ResultadoRangoDto,
  AsignacionParaleloDto,
  ParaleloDto,
  PeriodoDto} from '../models/horario.model';
import {
  MENSAJES_ERROR_HORARIO,
} from '../models/horario.model';

@Injectable({ providedIn: 'root' })
export class HorariosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}`;

  obtenerGrid(clave: ParaleloClave, lunes: string): Observable<GridDto> {
    const params = new HttpParams()
      .set('idPeriodo', clave.idPeriodo)
      .set('idNivel', clave.idNivel)
      .set('idSeccion', clave.idSeccion)
      .set('idModalidad', clave.idModalidad)
      .set('paralelo', clave.paralelo.trim())
      .set('lunes', lunes);

    return this.http.get<GridDto>(`${this.base}/horarios/grid`, { params });
  }

  validarConflicto(req: SolicitudConflictoDto): Observable<ResultadoConflictoDto> {
    return this.http.post<ResultadoConflictoDto>(`${this.base}/horarios/validar-conflicto`, req);
  }

  crearCelda(req: CrearCeldaDto): Observable<CeldaCreadaDto> {
    return this.http.post<CeldaCreadaDto>(`${this.base}/horarios`, req);
  }

  actualizarCelda(idHorario: number, req: CrearCeldaDto): Observable<CeldaCreadaDto> {
    return this.http.put<CeldaCreadaDto>(`${this.base}/horarios/${idHorario}`, req);
  }

  desactivarCelda(idHorario: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/horarios/${idHorario}`);
  }

  validarTopeRango(desdeStr: string, hastaStr: string): { valido: boolean; error?: string; dias?: number } {
    const d1 = new Date(desdeStr);
    const d2 = new Date(hastaStr);
    if (isNaN(d1.getTime()) || isNaN(d2.getTime()) || d1 > d2) {
      return { valido: false, error: MENSAJES_ERROR_HORARIO['RANGO_INVALIDO'] };
    }
    const diffMs = d2.getTime() - d1.getTime();
    const dias = Math.floor(diffMs / (1000 * 60 * 60 * 24)) + 1;
    if (dias > 112) { // 16 semanas * 7 días
      return { valido: false, error: MENSAJES_ERROR_HORARIO['RANGO_EXCEDE_TOPE'] };
    }
    return { valido: true, dias };
  }

  replicarRango(req: OperacionRangoDto): Observable<ResultadoRangoDto> {
    const val = this.validarTopeRango(req.desde, req.hasta);
    if (!val.valido) {
      return throwError(() => new Error(val.error));
    }
    return this.http.post<ResultadoRangoDto>(`${this.base}/horarios/replicar-rango`, req);
  }

  actualizarRango(req: OperacionRangoDto): Observable<ResultadoRangoDto> {
    const val = this.validarTopeRango(req.desde, req.hasta);
    if (!val.valido) {
      return throwError(() => new Error(val.error));
    }
    return this.http.put<ResultadoRangoDto>(`${this.base}/horarios/rango`, req);
  }

  eliminarRango(req: OperacionRangoDto): Observable<ResultadoRangoDto> {
    const val = this.validarTopeRango(req.desde, req.hasta);
    if (!val.valido) {
      return throwError(() => new Error(val.error));
    }
    return this.http.delete<ResultadoRangoDto>(`${this.base}/horarios/rango`, { body: req });
  }

  obtenerPeriodos(soloVigentes: boolean = true): Observable<PeriodoDto[]> {
    const params = new HttpParams().set('soloVigentes', soloVigentes);
    return this.http.get<PeriodoDto[]>(`${this.base}/periodos`, { params });
  }

  obtenerParalelosInspector(idPeriodo?: string, soloVigentes: boolean = true): Observable<ParaleloDto[]> {
    let params = new HttpParams().set('soloVigentes', soloVigentes);
    if (idPeriodo) {
      params = params.set('idPeriodo', idPeriodo);
    }
    return this.http.get<ParaleloDto[]>(`${this.base}/paralelos`, { params });
  }

  obtenerAsignaciones(clave: ParaleloClave): Observable<AsignacionParaleloDto[]> {
    const params = new HttpParams()
      .set('idPeriodo', clave.idPeriodo)
      .set('idNivel', clave.idNivel)
      .set('idSeccion', clave.idSeccion)
      .set('idModalidad', clave.idModalidad)
      .set('paralelo', clave.paralelo.trim());

    return this.http.get<AsignacionParaleloDto[]>(`${this.base}/paralelos/asignaciones`, { params });
  }
}
