import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';

import { PasarListaStore } from './pasar-lista.store';
import { EstadoAsistencia } from '@features/docente/models/asistencia.model';
import { environment } from '@env/environment';

describe('PasarListaStore', () => {
  let store: PasarListaStore;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        PasarListaStore,
      ],
    });
    store = TestBed.inject(PasarListaStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function cargarYPoblar() {
    store.cargar(23229, '2026-08-07', 'Clase del día');
    const alumnosReq = httpMock.expectOne(`${environment.apiUrl}/paralelos/23229/alumnos`);
    alumnosReq.flush([
      { idMatricula: 1, idAlumno: 'a1', apellidos: 'APELLIDO A', nombres: 'NOMBRE A', retirado: false, esOyente: false },
      { idMatricula: 2, idAlumno: 'a2', apellidos: 'APELLIDO B', nombres: 'NOMBRE B', retirado: false, esOyente: false },
      { idMatricula: 3, idAlumno: 'a3', apellidos: 'APELLIDO C', nombres: 'NOMBRE C', retirado: true,  esOyente: false }, // retirado
    ]);
    const sesionReq = httpMock.expectOne(`${environment.apiUrl}/paralelos/23229/sesiones`);
    sesionReq.flush({
      idSesion: 9001,
      idAsignacion: 23229,
      fecha: '2026-08-07',
      numeroBloque: 1,
      tema: 'Clase del día',
      observacion: null,
      estado: 'borrador',
      fechaCierre: null,
      asistencias: [],
    });
  }

  it('carga la nómina excluyendo retirados y los pone en presente', () => {
    cargarYPoblar();
    expect(store.totalAlumnos()).toBe(2);
    expect(store.totalPresentes()).toBe(2);
    expect(store.hayCambios()).toBe(false);
  });

  it('ciclarEstado rota presente → ausente → atraso → justificado → presente', () => {
    cargarYPoblar();
    store.ciclarEstado(1);
    expect(store.marcas()[0].estado).toBe(EstadoAsistencia.Ausente);
    expect(store.hayCambios()).toBe(true);

    store.ciclarEstado(1);
    expect(store.marcas()[0].estado).toBe(EstadoAsistencia.Atraso);
    expect(store.marcas()[0].minutosAtraso).toBe(5);

    store.ciclarEstado(1);
    expect(store.marcas()[0].estado).toBe(EstadoAsistencia.Justificado);
    expect(store.marcas()[0].minutosAtraso).toBeNull();

    store.ciclarEstado(1);
    expect(store.marcas()[0].estado).toBe(EstadoAsistencia.Presente);
  });

  it('guardar envía todas las marcas en una sola petición y limpia cambios', async () => {
    cargarYPoblar();
    store.ciclarEstado(1); // 1 ausente
    store.ciclarEstado(2); // 2 ausente
    store.ciclarEstado(2); // 2 atraso

    const promise = store.guardar();
    const req = httpMock.expectOne(`${environment.apiUrl}/sesiones/9001/asistencias`);
    expect(req.request.method).toBe('POST');
    const body = req.request.body as { marcas: Array<{ idMatricula: number; estado: string }> };
    expect(body.marcas).toHaveLength(2);
    expect(body.marcas.find((m) => m.idMatricula === 1)?.estado).toBe(EstadoAsistencia.Ausente);
    expect(body.marcas.find((m) => m.idMatricula === 2)?.estado).toBe(EstadoAsistencia.Atraso);

    req.flush({
      idSesion: 9001,
      idAsignacion: 23229,
      fecha: '2026-08-07',
      numeroBloque: 1,
      tema: 'Clase del día',
      observacion: null,
      estado: 'borrador',
      fechaCierre: null,
      asistencias: body.marcas.map((m) => ({
        idAsistencia: 100,
        idMatricula: m.idMatricula,
        estado: m.estado,
        minutosAtraso: m.estado === EstadoAsistencia.Atraso ? 5 : null,
        observacion: null,
      })),
    });

    await promise;
    expect(store.hayCambios()).toBe(false);
    expect(store.errorGuardar()).toBeNull();
    expect(store.ultimoGuardado()).not.toBeNull();
  });

  it('guardar con fallo conserva el estado local y muestra error', async () => {
    cargarYPoblar();
    store.ciclarEstado(1);

    const promise = store.guardar();
    const req = httpMock.expectOne(`${environment.apiUrl}/sesiones/9001/asistencias`);
    req.flush(
      { codigo: 'ERROR_INTERNO', mensaje: 'fail', detalles: null, traceId: 't' },
      { status: 500, statusText: 'Server Error' },
    );

    await expect(promise).rejects.toThrow();
    expect(store.hayCambios()).toBe(true);
    expect(store.errorGuardar()).toMatch(/No se pudo guardar/);
  });

  it('aplicarSesion sobrescribe el estado local con lo del backend', () => {
    cargarYPoblar();
    store.aplicarSesion({
      idSesion: 9001,
      idAsignacion: 23229,
      fecha: '2026-08-07',
      numeroBloque: 1,
      tema: 'Clase del día',
      observacion: null,
      estado: 'borrador',
      fechaCierre: null,
      asistencias: [
        { idAsistencia: 1, idMatricula: 1, estado: EstadoAsistencia.Ausente, minutosAtraso: null, observacion: null },
      ],
    });

    const m1 = store.marcas().find((m) => m.idMatricula === 1)!;
    expect(m1.estado).toBe(EstadoAsistencia.Ausente);
    expect(m1.modificado).toBe(false);
  });
});
