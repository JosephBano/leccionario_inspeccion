import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';

import { MiHorarioStore } from './mi-horario.store';
import type { BloqueMiHorario } from '@features/docente/models/mi-horario.model';

function bloque(over: Partial<BloqueMiHorario>): BloqueMiHorario {
  return {
    idAsignacion: 100,
    tipoLicencia: 'C',
    jornada: 'MATUTINA',
    paralelo: 'A',
    asignatura: 'Normativa de tránsito',
    fecha: '2026-08-03',
    dia: 'Lunes',
    idHorarioInicio: 1,
    numeroBloque: 1,
    horaInicio: '07:00',
    horaFin: '09:00',
    franjasPlanificadas: 2,
    minutosPlanificados: 120,
    estado: 'Pendiente',
    idSesion: null,
    diasRetraso: 2,
    ...over,
  };
}

describe('MiHorarioStore', () => {
  let store: MiHorarioStore;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [MiHorarioStore, provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(MiHorarioStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('pide la semana del lunes actual y guarda los bloques', () => {
    store.irASemanaDe('2026-08-05');
    const req = httpMock.expectOne(
      (r) => r.url.endsWith('/mi-horario')
        && r.params.get('desde') === '2026-08-03'
        && r.params.get('hasta') === '2026-08-09',
    );
    req.flush([bloque({})]);

    expect(store.lunes()).toBe('2026-08-03');
    expect(store.bloques()).toHaveLength(1);
    expect(store.cargando()).toBe(false);
  });

  it('cambiarSemana(1) avanza siete días y vuelve a pedir', () => {
    store.irASemanaDe('2026-08-05');
    httpMock.expectOne((r) => r.url.endsWith('/mi-horario')).flush([]);

    store.cambiarSemana(1);
    const req = httpMock.expectOne(
      (r) => r.params.get('desde') === '2026-08-10' && r.params.get('hasta') === '2026-08-16',
    );
    req.flush([]);

    expect(store.lunes()).toBe('2026-08-10');
  });

  it('agrupa los bloques en filas por franja y columnas por día', () => {
    store.irASemanaDe('2026-08-05');
    httpMock.expectOne((r) => r.url.endsWith('/mi-horario')).flush([
      bloque({ fecha: '2026-08-03', horaInicio: '07:00', horaFin: '09:00' }),
      bloque({ fecha: '2026-08-05', horaInicio: '07:00', horaFin: '09:00', idHorarioInicio: 3 }),
      bloque({ fecha: '2026-08-05', horaInicio: '10:00', horaFin: '11:00', idHorarioInicio: 4 }),
    ]);

    const filas = store.filas();
    expect(filas).toHaveLength(2);
    expect(filas[0].horaInicio).toBe('07:00');
    expect(filas[0].celdas[0]?.idHorarioInicio).toBe(1);   // lunes
    expect(filas[0].celdas[2]?.idHorarioInicio).toBe(3);   // miércoles
    expect(filas[0].celdas[1]).toBeNull();                 // martes
    expect(filas[1].horaInicio).toBe('10:00');
  });

  it('guarda un mensaje de error si la petición falla', () => {
    store.irASemanaDe('2026-08-05');
    httpMock.expectOne((r) => r.url.endsWith('/mi-horario'))
      .flush({ codigo: 'RANGO_EXCEDE_TOPE', mensaje: 'x' }, { status: 422, statusText: 'Unprocessable' });

    expect(store.error()).toBeTruthy();
    expect(store.cargando()).toBe(false);
  });
});
