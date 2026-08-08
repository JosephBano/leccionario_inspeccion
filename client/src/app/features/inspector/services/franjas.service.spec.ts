import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';

import { FranjasService } from './franjas.service';
import { environment } from '@env/environment';
import { obtenerMensajeError } from '../models/horario.model';

describe('FranjasService', () => {
  let service: FranjasService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        FranjasService,
      ],
    });
    service = TestBed.inject(FranjasService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('FRANJA_EN_USO se propaga como código y se traduce con obtenerMensajeError', () => {
    service.desactivar(99).subscribe({
      next: () => expect.fail('Debió responder 409'),
      error: (err) => {
        expect(err.status).toBe(409);
        expect(err.error.codigo).toBe('FRANJA_EN_USO');
        const msg = obtenerMensajeError(err.error.codigo);
        expect(msg).toMatch(/está en uso/);
      },
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/franjas/99`);
    req.flush(
      { codigo: 'FRANJA_EN_USO', mensaje: 'Mensaje crudo del backend' },
      { status: 409, statusText: 'Conflict' }
    );
  });
});
