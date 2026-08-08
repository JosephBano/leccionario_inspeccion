import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import type { Observable } from 'rxjs';
import { environment } from '@env/environment';
import type { BloqueAgendaDto } from '../models/agenda.model';

@Injectable({ providedIn: 'root' })
export class AgendaService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}`;

  obtenerAgenda(idAsignacion: number, desde: string, hasta: string): Observable<BloqueAgendaDto[]> {
    const params = new HttpParams()
      .set('desde', desde)
      .set('hasta', hasta);

    return this.http.get<BloqueAgendaDto[]>(`${this.base}/paralelos/${idAsignacion}/agenda`, { params });
  }
}
