import { TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';

import { LoginPage } from './login.page';
import { AuthService } from '@core/auth/services/auth.service';
import { environment } from '@env/environment';
import { ErrorCodes } from '@core/models/api-error.model';

describe('LoginPage', () => {
  let component: LoginPage;
  let httpMock: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [LoginPage, ReactiveFormsModule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => null } } },
        },
      ],
    });

    component = TestBed.createComponent(LoginPage).componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);

    if (typeof localStorage !== 'undefined') localStorage.clear();

    vi.spyOn(console, 'warn').mockImplementation(() => {});
    vi.spyOn(auth, 'logout').mockResolvedValue();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('mapea 401 con CREDENCIALES_INVALIDAS al mensaje del cliente', async () => {
    component.form.setValue({ username: '1724649338', password: 'wrong' });

    const promise = component.onSubmit();
    const req = httpMock.expectOne(`${environment.apiUrl}/auth/login`);
    req.flush(
      {
        codigo: ErrorCodes.CREDENCIALES_INVALIDAS,
        mensaje: 'Credenciales inválidas.',
        detalles: null,
        traceId: 't',
      },
      { status: 401, statusText: 'Unauthorized' },
    );

    await promise;

    expect(component.error()?.codigo).toBe(ErrorCodes.CREDENCIALES_INVALIDAS);
    expect(component.error()?.mensaje).toMatch(/incorrectas/i);
    expect(component.loading()).toBe(false);
  });

  it('login exitoso navega al destino', async () => {
    component.form.setValue({ username: '1724649338', password: 'secret' });

    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockImplementation(async () => true);
    const promise = component.onSubmit();
    const req = httpMock.expectOne(`${environment.apiUrl}/auth/login`);
    req.flush({
      accessToken: 'a',
      refreshToken: 'r',
      expiresIn: 28800,
      usuario: {
        idSigafi: '1724649338',
        nombre: 'X',
        email: null,
        tipoUsuario: 'profesor',
        roles: ['cplec_docente'],
      },
    });

    await promise;
    expect(navigateSpy).toHaveBeenCalledWith('/');
  });
});
