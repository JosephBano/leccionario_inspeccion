import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import type { Observable } from 'rxjs';
import { environment } from '@env/environment';
import type { SesionTardiaDto, DiaSinRegistrarDto } from '../models/horario.model';

@Injectable({ providedIn: 'root' })
export class ReportesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/reportes`;

  obtenerSesionesTardias(desde: string, hasta: string): Observable<SesionTardiaDto[]> {
    const params = new HttpParams().set('desde', desde).set('hasta', hasta);
    return this.http.get<SesionTardiaDto[]>(`${this.base}/sesiones-tardias`, { params });
  }

  obtenerDiasSinRegistrar(desde: string, hasta: string): Observable<DiaSinRegistrarDto[]> {
    const params = new HttpParams().set('desde', desde).set('hasta', hasta);
    return this.http.get<DiaSinRegistrarDto[]>(`${this.base}/dias-sin-registrar`, { params });
  }
}
