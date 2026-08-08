import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import type { Observable } from 'rxjs';
import { environment } from '@env/environment';
import type { BloqueMiHorario } from '../models/mi-horario.model';

@Injectable({ providedIn: 'root' })
export class MiHorarioService {
  private readonly http = inject(HttpClient);

  obtener(desde: string, hasta: string): Observable<BloqueMiHorario[]> {
    const params = new HttpParams().set('desde', desde).set('hasta', hasta);
    return this.http.get<BloqueMiHorario[]>(`${environment.apiUrl}/mi-horario`, { params });
  }
}
