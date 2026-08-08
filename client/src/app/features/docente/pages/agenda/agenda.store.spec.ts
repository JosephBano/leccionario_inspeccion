import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';

import { AgendaStore } from './agenda.store';
import type { BloqueAgendaDto } from '../../models/agenda.model';

describe('AgendaStore', () => {
  let store: AgendaStore;
  let httpMock: HttpTestingController;

  const bloquesMock: BloqueAgendaDto[] = [
    {
      fecha: '2026-08-10',
      dia: 'Lunes',
      idHorarioInicio: 101,
      numeroBloque: 1,
      horaInicio: '08:00',
      horaFin: '09:00',
      franjasPlanificadas: 1,
      minutosPlanificados: 60,
      estado: 'Pendiente',
      idSesion: null,
      diasRetraso: 0,
    },
    {
      fecha: '2026-08-11',
      dia: 'Martes',
      idHorarioInicio: 102,
      numeroBloque: 1,
      horaInicio: '09:00',
      horaFin: '10:00',
      franjasPlanificadas: 1,
      minutosPlanificados: 60,
      estado: 'Borrador',
      idSesion: 501,
      diasRetraso: 0,
    },
    {
      fecha: '2026-08-12',
      dia: 'Miercoles',
      idHorarioInicio: 103,
      numeroBloque: 1,
      horaInicio: '10:00',
      horaFin: '11:00',
      franjasPlanificadas: 1,
      minutosPlanificados: 60,
      estado: 'Cerrada',
      idSesion: 502,
      diasRetraso: 2,
    },
    {
      fecha: '2026-08-14',
      dia: 'Viernes',
      idHorarioInicio: 104,
      numeroBloque: 1,
      horaInicio: '11:00',
      horaFin: '12:00',
      franjasPlanificadas: 1,
      minutosPlanificados: 60,
      estado: 'Futura',
      idSesion: null,
      diasRetraso: 0,
    },
  ];

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        AgendaStore,
      ],
    });
    store = TestBed.inject(AgendaStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('clasifica correctamente los bloques por estado (Pendiente / Borrador / Cerrada / Futura)', () => {
    store.inicializar(123, null, '2026-08-10');

    const req = httpMock.expectOne((r) => r.url.includes('/paralelos/123/agenda'));
    req.flush(bloquesMock);

    const b = store.bloques();
    expect(b).toHaveLength(4);
    expect(b[0].estado).toBe('Pendiente');
    expect(b[1].estado).toBe('Borrador');
    expect(b[2].estado).toBe('Cerrada');
    expect(b[3].estado).toBe('Futura');
  });

  it('un bloque Futura no es navegable (estado Futura en store)', () => {
    store.inicializar(123, null, '2026-08-10');
    const req = httpMock.expectOne((r) => r.url.includes('/paralelos/123/agenda'));
    req.flush(bloquesMock);

    const bloqueFutura = store.bloques().find((x) => x.estado === 'Futura')!;
    expect(bloqueFutura.estado).toBe('Futura');
    expect(bloqueFutura.idSesion).toBeNull();
  });
});
