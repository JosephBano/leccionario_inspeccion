import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import type { Observable } from 'rxjs';
import { environment } from '@env/environment';
import type { FranjaDto, CrearFranjaDto } from '../models/horario.model';

@Injectable({ providedIn: 'root' })
export class FranjasService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/franjas`;

  listar(): Observable<FranjaDto[]> {
    return this.http.get<FranjaDto[]>(this.base);
  }

  crear(req: CrearFranjaDto): Observable<FranjaDto> {
    return this.http.post<FranjaDto>(this.base, req);
  }

  actualizar(idhora: number, req: CrearFranjaDto): Observable<FranjaDto> {
    return this.http.put<FranjaDto>(`${this.base}/${idhora}`, req);
  }

  desactivar(idhora: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${idhora}`);
  }
}
