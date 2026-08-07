import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import type { Observable } from 'rxjs';

import { environment } from '@env/environment';
import type {
  CrearSesionRequest,
  RegistrarAsistenciaRequest,
  SesionDto,
} from '@features/docente/models/asistencia.model';

@Injectable({ providedIn: 'root' })
export class AsistenciaService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}`;

  /**
   * POST /api/paralelos/{idAsignacion}/sesiones — idempotente por tupla.
   * Devuelve 200 con la sesión existente si ya estaba creada.
   */
  crearSesion(idAsignacion: number, body: CrearSesionRequest): Observable<SesionDto> {
    return this.http.post<SesionDto>(`${this.base}/paralelos/${idAsignacion}/sesiones`, body);
  }

  /**
   * GET /api/sesiones/{idSesion}
   */
  obtenerSesion(idSesion: number): Observable<SesionDto> {
    return this.http.get<SesionDto>(`${this.base}/sesiones/${idSesion}`);
  }

  /**
   * POST /api/sesiones/{idSesion}/asistencias — idempotente, transacción + historial.
   */
  registrarAsistencia(idSesion: number, body: RegistrarAsistenciaRequest): Observable<SesionDto> {
    return this.http.post<SesionDto>(`${this.base}/sesiones/${idSesion}/asistencias`, body);
  }

  /**
   * POST /api/sesiones/{idSesion}/cerrar — docente o inspector.
   */
  cerrarSesion(idSesion: number): Observable<void> {
    return this.http.post<void>(`${this.base}/sesiones/${idSesion}/cerrar`, {});
  }
}
