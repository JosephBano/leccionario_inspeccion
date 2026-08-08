import type { ComponentRef } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';

import { PasarListaPage } from './pasar-lista.page';
import { PasarListaStore } from './pasar-lista.store';
import { environment } from '@env/environment';

describe('PasarListaPage', () => {
  let fixture: ComponentFixture<PasarListaPage>;
  let componentRef: ComponentRef<PasarListaPage>;
  let httpMock: HttpTestingController;
  let router: Router;
  let snackBar: MatSnackBar;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PasarListaPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
      ],
    });

    fixture = TestBed.createComponent(PasarListaPage);
    componentRef = fixture.componentRef;
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    snackBar = TestBed.inject(MatSnackBar);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('redirecciona a agenda si idHorarioInicio es nulo, invalido o 0', () => {
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    componentRef.setInput('idAsignacion', 100);
    componentRef.setInput('idHorarioInicio', null);

    fixture.detectChanges();

    expect(snackBar.open).toHaveBeenCalledWith('Elige el bloque de clase para pasar lista.', 'OK', expect.any(Object));
    expect(navigateSpy).toHaveBeenCalledWith(['/paralelos', 100, 'agenda']);
  });

  it('muestra el banner de error cuando la API responde SIN_HORARIO', () => {
    componentRef.setInput('idAsignacion', 100);
    componentRef.setInput('idHorarioInicio', 77);

    fixture.detectChanges();

    // 1. Pide alumnos
    httpMock.expectOne(`${environment.apiUrl}/paralelos/100/alumnos`).flush([]);
    // 2. Pide crear sesión y falla con SIN_HORARIO
    httpMock.expectOne(`${environment.apiUrl}/paralelos/100/sesiones`)
      .flush({ codigo: 'SIN_HORARIO', mensaje: 'x' }, { status: 422, statusText: 'Unprocessable' });

    fixture.detectChanges();

    const banner: HTMLElement = fixture.nativeElement.querySelector('.error-banner');
    expect(banner).not.toBeNull();
    expect(banner.textContent).toContain('Este paralelo no tiene horario planificado');
  });

  it('si falla el guardado conserva la lista y el botón para reintentar', async () => {
    componentRef.setInput('idAsignacion', 100);
    componentRef.setInput('idHorarioInicio', 77);

    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiUrl}/paralelos/100/alumnos`).flush([
      { idMatricula: 1, idAlumno: 'a1', apellidos: 'APELLIDO A', nombres: 'NOMBRE A', retirado: false, esOyente: false },
    ]);
    httpMock.expectOne(`${environment.apiUrl}/paralelos/100/sesiones`).flush({
      idSesion: 9001,
      idAsignacion: 100,
      fecha: '2026-08-07',
      numeroBloque: 1,
      tema: 'Clase del día',
      observacion: null,
      estado: 'borrador',
      fechaCierre: null,
      asistencias: [],
    });
    fixture.detectChanges();

    const store = fixture.debugElement.injector.get(PasarListaStore);
    store.ciclarEstado(1);
    const guardado = store.guardar().catch(() => undefined);
    httpMock.expectOne(`${environment.apiUrl}/sesiones/9001/asistencias`)
      .flush({ codigo: 'ERROR_INTERNO', mensaje: 'x' }, { status: 500, statusText: 'Server Error' });
    await guardado;

    fixture.detectChanges();

    // El error de guardado no puede secuestrar la pantalla: la lista y el botón
    // de reintentar siguen ahí, porque el mensaje promete que "la lista se conservó".
    const error: HTMLElement = fixture.nativeElement.querySelector('.error');
    expect(error).not.toBeNull();
    expect(error.textContent).toContain('reintenta');
    expect(fixture.nativeElement.querySelector('.error-banner')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.lista li')).toHaveLength(1);

    const textos = [...fixture.nativeElement.querySelectorAll('button')].map(
      (b) => (b as HTMLElement).textContent ?? '',
    );
    expect(textos.some((t) => t.includes('Guardar lista'))).toBe(true);
  });
});
