import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';

import { HorariosStore } from './horarios.store';
import { environment } from '@env/environment';
import type { DiaGridDto, FranjaDto, CeldaGridDto } from '../../models/horario.model';
import type { ParaleloClave } from '@shared/paralelo-selector/paralelo-selector';

describe('HorariosStore', () => {
  let store: HorariosStore;
  let httpMock: HttpTestingController;

  const claveTest: ParaleloClave = {
    idPeriodo: 'OCC2025',
    idNivel: 35,
    idSeccion: 1,
    idModalidad: 1,
    paralelo: 'A',
  };

  const diaHab: DiaGridDto = {
    dia: 'Lunes',
    fecha: '2026-08-10',
    idFecha: 101,
    habilitado: true,
    motivo: null,
  };

  const diaDeshab: DiaGridDto = {
    dia: 'Martes',
    fecha: '2026-08-11',
    idFecha: null,
    habilitado: false,
    motivo: 'Sin calendario',
  };

  const franjaTest: FranjaDto = {
    idhora: 1,
    horaInicio: '08:00',
    horaFin: '09:00',
  };

  const celdaExistente: CeldaGridDto = {
    idHorario: 50,
    idAsignacion: 200,
    idhora: 1,
    dia: 'Lunes',
    nombreDocente: 'Perez Juan',
    tipoBloque: null,
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        HorariosStore,
      ],
    });
    store = TestBed.inject(HorariosStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('celda de día habilitado: false no abre el panel', () => {
    store.abrirPanelCelda(diaDeshab, franjaTest);
    expect(store.panelAbierto()).toBe(false);
    expect(store.celdaSeleccionada()).toBeNull();
  });

  it('conflicto bloqueante deja puedeGuardarEnPanel en false', () => {
    store.abrirPanelCelda(diaHab, franjaTest);
    store.validarConflicto(200);

    const req = httpMock.expectOne(`${environment.apiUrl}/horarios/validar-conflicto`);
    req.flush({
      hayBloqueantes: true,
      bloqueantes: [
        {
          tipo: 'DOCENTE_OCUPADO',
          severidad: 'Bloqueante',
          mensaje: 'El docente ya tiene clase a esta hora',
          idHorarioConflicto: 99,
        },
      ],
      advertencias: [],
    });

    expect(store.puedeGuardarEnPanel()).toBe(false);
  });

  it('advertencia sí permite guardar con confirmación explícita', () => {
    store.abrirPanelCelda(diaHab, franjaTest);
    store.validarConflicto(200);

    const reqVal = httpMock.expectOne(`${environment.apiUrl}/horarios/validar-conflicto`);
    reqVal.flush({
      hayBloqueantes: false,
      bloqueantes: [],
      advertencias: [
        {
          tipo: 'CHOQUE_EXTERNO',
          severidad: 'Advertencia',
          mensaje: 'Choque contra horario de otra carrera',
          idHorarioConflicto: 88,
          carrera: 'CARRERA B',
        },
      ],
    });

    expect(store.puedeGuardarEnPanel()).toBe(true);

    store.guardarCelda(200, true);
    const reqSave = httpMock.expectOne(`${environment.apiUrl}/horarios`);
    expect(reqSave.request.body.confirmarAdvertencias).toBe(true);
    reqSave.flush({ idHorario: 501, advertencias: [] });
  });

  it('cambiar de semana recarga el grid', () => {
    store.seleccionarParalelo(claveTest);
    httpMock.expectOne((req) => req.url.includes('/horarios/grid')).flush({ franjas: [], dias: [], celdas: [] });
    httpMock.expectOne((req) => req.url.includes('/paralelos/asignaciones')).flush([]);

    const lunesPrev = store.lunes();
    store.cambiarSemana(1); // +1 semana

    expect(store.lunes()).not.toBe(lunesPrev);
    const reqGrid = httpMock.expectOne((req) => req.url.includes('/horarios/grid'));
    expect(reqGrid.request.params.get('lunes')).toBe(store.lunes());
    reqGrid.flush({ franjas: [], dias: [], celdas: [] });
  });

  it('editar envía su propio idHorarioExcluir', () => {
    store.abrirPanelCelda(diaHab, franjaTest, celdaExistente);
    const req = httpMock.expectOne(`${environment.apiUrl}/horarios/validar-conflicto`);
    expect(req.request.body.idHorarioExcluir).toBe(50);
    req.flush({ bloqueantes: [], advertencias: [] });
  });
});
