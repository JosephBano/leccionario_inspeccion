import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';

import { authInterceptor } from './auth.interceptor';
import { AuthService } from '@core/auth/services/auth.service';
import { environment } from '@env/environment';
import { Roles } from '@core/auth/models/roles';

describe('authInterceptor', () => {
  let httpMock: HttpTestingController;
  let auth: AuthService;
  let http: HttpClient;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });

    auth = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    http = TestBed.inject(HttpClient);

    if (typeof localStorage !== 'undefined') localStorage.clear();

    vi.spyOn(console, 'warn').mockImplementation(() => {});
    vi.spyOn(auth, 'logout').mockResolvedValue();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('adjunta Authorization cuando hay access token', async () => {
    auth.hydrateFromStorage({
      access: 'access-1',
      refresh: 'refresh-1',
      usuario: {
        idSigafi: '1804567890',
        nombre: 'X',
        email: null,
        tipoUsuario: 'profesor',
        roles: [Roles.docente],
      },
    });

    const promise = firstValueFrom(http.get(`${environment.apiUrl}/mis-paralelos`));
    const req = httpMock.expectOne(`${environment.apiUrl}/mis-paralelos`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer access-1');
    req.flush({ items: [] });
    await promise;
  });

  it('ante 401 llama a refresh una vez y reintenta con el nuevo token', async () => {
    auth.hydrateFromStorage({
      access: 'old-access',
      refresh: 'refresh-1',
      usuario: {
        idSigafi: '1804567890',
        nombre: 'X',
        email: null,
        tipoUsuario: 'profesor',
        roles: [Roles.docente],
      },
    });

    const refreshSpy = vi.spyOn(auth, 'refresh').mockImplementation(async () => {
      // Simulamos el contrato real: el refresh exitoso reemplaza el access token.
      const resp = {
        accessToken: 'new-access',
        refreshToken: 'new-refresh',
        expiresIn: 28800,
      };
      auth['_accessToken'].set(resp.accessToken);
      auth['_refreshToken'].set(resp.refreshToken);
      return resp;
    });

    const promise = firstValueFrom(http.get(`${environment.apiUrl}/mis-paralelos`));

    const firstReq = httpMock.expectOne(`${environment.apiUrl}/mis-paralelos`);
    expect(firstReq.request.headers.get('Authorization')).toBe('Bearer old-access');
    firstReq.flush(
      { codigo: 'TOKEN_EXPIRADO', mensaje: 'x', detalles: null, traceId: 't' },
      { status: 401, statusText: 'Unauthorized' },
    );

    // El refresh corre en microtask; esperamos al reintento antes de matchear.
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    const retryReq = httpMock.expectOne(`${environment.apiUrl}/mis-paralelos`);
    expect(retryReq.request.headers.get('Authorization')).toBe('Bearer new-access');
    retryReq.flush({ items: [] });
    await promise;

    expect(refreshSpy).toHaveBeenCalledTimes(1);
  });

  it('no reintenta un 403 — es decisión del servidor', async () => {
    auth.hydrateFromStorage({
      access: 'a',
      refresh: 'r',
      usuario: {
        idSigafi: '1804567890',
        nombre: 'X',
        email: null,
        tipoUsuario: 'profesor',
        roles: [Roles.docente],
      },
    });

    const refreshSpy = vi.spyOn(auth, 'refresh');

    const promise = firstValueFrom(http.get(`${environment.apiUrl}/mis-paralelos`));
    const req = httpMock.expectOne(`${environment.apiUrl}/mis-paralelos`);
    req.flush(
      { codigo: 'DISTRIBUTIVO_AJENO', mensaje: 'x', detalles: null, traceId: 't' },
      { status: 403, statusText: 'Forbidden' },
    );

    await expect(promise).rejects.toBeInstanceOf(HttpErrorResponse);
    expect(refreshSpy).not.toHaveBeenCalled();
  });

  it('no adjunta Authorization en URLs que no son de la API', async () => {
    const promise = firstValueFrom(http.get('https://otro.example.com/data'));
    const req = httpMock.expectOne('https://otro.example.com/data');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
    await promise;
  });
});
