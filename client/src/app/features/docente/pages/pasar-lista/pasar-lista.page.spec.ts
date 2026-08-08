import type { ComponentRef } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';

import { PasarListaPage } from './pasar-lista.page';
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
});
