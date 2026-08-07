import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';

import { DistributivoService } from './distributivo.service';
import { environment } from '@env/environment';

describe('DistributivoService', () => {
  let service: DistributivoService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DistributivoService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('misParalelos envía idPeriodo solo cuando se pasa', () => {
    service.misParalelos('OCC2025').subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/mis-paralelos?idPeriodo=OCC2025`);
    expect(req.request.method).toBe('GET');
    req.flush({ items: [] });
  });

  it('misParalelos omite idPeriodo si no se pasa', () => {
    service.misParalelos().subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/mis-paralelos`);
    expect(req.request.params.has('idPeriodo')).toBe(false);
    req.flush({ items: [] });
  });

  it('alumnos pide la nómina del paralelo', () => {
    service.alumnos(23229).subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/paralelos/23229/alumnos`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('periodosPorNivel consulta el endpoint del backend', () => {
    service.periodosPorNivel().subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/periodos/por-nivel`);
    expect(req.request.method).toBe('GET');
    req.flush({ items: [] });
  });
});
