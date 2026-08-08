import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';

import { HorariosService } from './horarios.service';
import { environment } from '@env/environment';
import type { ParaleloClave } from '@shared/paralelo-selector/paralelo-selector';
import type { OperacionRangoDto } from '../models/horario.model';

describe('HorariosService', () => {
  let service: HorariosService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        HorariosService,
      ],
    });
    service = TestBed.inject(HorariosService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('el query del grid lleva la 5-tupla completa', () => {
    const clave: ParaleloClave = {
      idPeriodo: 'OCC2025',
      idNivel: 35,
      idSeccion: 2,
      idModalidad: 1,
      paralelo: ' B ',
    };

    service.obtenerGrid(clave, '2026-08-10').subscribe();

    const req = httpMock.expectOne((r) => r.url.includes('/horarios/grid'));
    expect(req.request.params.get('idPeriodo')).toBe('OCC2025');
    expect(req.request.params.get('idNivel')).toBe('35');
    expect(req.request.params.get('idSeccion')).toBe('2');
    expect(req.request.params.get('idModalidad')).toBe('1');
    expect(req.request.params.get('paralelo')).toBe('B'); // trimmed
    expect(req.request.params.get('lunes')).toBe('2026-08-10');

    req.flush({ franjas: [], dias: [], celdas: [] });
  });

  it('un rango de más de 16 semanas se rechaza antes de la petición HTTP', () => {
    const reqDto: OperacionRangoDto = {
      idAsignacion: 100,
      dia: 'Lunes',
      idhora: 1,
      desde: '2026-01-01',
      hasta: '2026-06-01', // > 112 días (151 días)
    };

    service.replicarRango(reqDto).subscribe({
      next: () => expect.fail('Debería haber rechazado por tope'),
      error: (err) => {
        expect(err.message).toMatch(/16 semanas/);
      },
    });

    // Verify HTTP call was NEVER made
    httpMock.expectNone(`${environment.apiUrl}/horarios/replicar-rango`);
  });
});
