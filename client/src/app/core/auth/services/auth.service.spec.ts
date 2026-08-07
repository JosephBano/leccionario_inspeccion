import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';

import { AuthService } from './auth.service';
import { environment } from '@env/environment';
import { ErrorCodes } from '@core/models/api-error.model';
import { Roles } from '@core/auth/models/roles';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);

    if (typeof localStorage !== 'undefined') {
      localStorage.clear();
    }
    if (typeof sessionStorage !== 'undefined') {
      sessionStorage.clear();
    }

    const navSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    void navSpy;
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('login', () => {
    it('almacena access token en memoria y refresh en localStorage', async () => {
      const promise = service.login({ username: '1724649338', password: 'secret' });

      const req = httpMock.expectOne(`${environment.apiUrl}/auth/login`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({
        username: '1724649338',
        password: 'secret',
      });
      req.flush({
        accessToken: 'access-1',
        refreshToken: 'refresh-1',
        expiresIn: 28800,
        usuario: {
          idSigafi: '1724649338',
          nombre: 'INSPECTOR PRUEBA',
          email: null,
          tipoUsuario: 'profesor',
          roles: [Roles.inspector],
        },
      });

      const response = await promise;

      expect(response.accessToken).toBe('access-1');
      expect(service.accessToken()).toBe('access-1');
      expect(service.refreshToken()).toBe('refresh-1');
      expect(service.isAuthenticated()).toBe(true);
      expect(service.usuario()?.idSigafi).toBe('1724649338');
      expect(service.esInspector()).toBe(true);
      expect(service.esDocente()).toBe(false);

      expect(localStorage.getItem('cplec.refresh')).toBe('refresh-1');
      expect(localStorage.getItem('cplec.access')).toBeNull();
    });

    it('mapea 401 con codigo CREDENCIALES_INVALIDAS a ApiError', async () => {
      const promise = service.login({ username: 'x', password: 'y' });
      const req = httpMock.expectOne(`${environment.apiUrl}/auth/login`);
      req.flush(
        {
          codigo: ErrorCodes.CREDENCIALES_INVALIDAS,
          mensaje: 'Credenciales inválidas.',
          detalles: null,
          traceId: 'abc',
        },
        { status: 401, statusText: 'Unauthorized' },
      );

      await expect(promise).rejects.toMatchObject({
        codigo: ErrorCodes.CREDENCIALES_INVALIDAS,
        status: 401,
      });
      expect(service.isAuthenticated()).toBe(false);
    });
  });

  describe('refreshToken', () => {
    it('intercambia el refresh token y rota el valor guardado', async () => {
      service.hydrateFromStorage({
        access: 'old-access',
        refresh: 'old-refresh',
        usuario: {
          idSigafi: '1804567890',
          nombre: 'DOCENTE PRUEBA',
          email: null,
          tipoUsuario: 'profesor',
          roles: [Roles.docente],
        },
      });

      const promise = service.refresh();
      const req = httpMock.expectOne(`${environment.apiUrl}/auth/refresh`);
      expect(req.request.body).toEqual({ refreshToken: 'old-refresh' });
      req.flush({
        accessToken: 'new-access',
        refreshToken: 'new-refresh',
        expiresIn: 28800,
      });

      await promise;

      expect(service.accessToken()).toBe('new-access');
      expect(service.refreshToken()).toBe('new-refresh');
    });

    it('si el refresh falla con 401, limpia la sesión y rechaza', async () => {
      service.hydrateFromStorage({
        access: 'a',
        refresh: 'b',
        usuario: {
          idSigafi: '1804567890',
          nombre: 'X',
          email: null,
          tipoUsuario: 'profesor',
          roles: [Roles.docente],
        },
      });

      const promise = service.refresh();
      const req = httpMock.expectOne(`${environment.apiUrl}/auth/refresh`);
      req.flush(
        { codigo: 'TOKEN_EXPIRADO', mensaje: 'x', detalles: null, traceId: 't' },
        { status: 401, statusText: 'Unauthorized' },
      );

      await expect(promise).rejects.toBeInstanceOf(HttpErrorResponse);
      expect(service.isAuthenticated()).toBe(false);
      expect(localStorage.getItem('cplec.refresh')).toBeNull();
    });

    it('sin refresh token en storage, rechaza sin llamar a la red', async () => {
      await expect(service.refresh()).rejects.toThrow(/no hay refresh token/i);
      httpMock.expectNone(`${environment.apiUrl}/auth/refresh`);
    });
  });

  describe('logout', () => {
    it('envía el refresh token al backend y limpia el estado local', async () => {
      service.hydrateFromStorage({
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

      const promise = service.logout();
      const req = httpMock.expectOne(`${environment.apiUrl}/auth/logout`);
      expect(req.request.body).toEqual({ refreshToken: 'r' });
      req.flush(null, { status: 204, statusText: 'No Content' });
      await promise;

      expect(service.isAuthenticated()).toBe(false);
      expect(service.accessToken()).toBeNull();
      expect(localStorage.getItem('cplec.refresh')).toBeNull();
    });

    it('aunque el backend falle, limpia el estado local y navega a /login', async () => {
      service.hydrateFromStorage({
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

      const promise = service.logout();
      const req = httpMock.expectOne(`${environment.apiUrl}/auth/logout`);
      req.flush('boom', { status: 500, statusText: 'Server Error' });
      await promise;

      expect(service.isAuthenticated()).toBe(false);
    });
  });

  describe('estado derivado', () => {
    it('esDocente y esInspector se calculan desde los roles', () => {
      service.hydrateFromStorage({
        access: 'a',
        refresh: 'r',
        usuario: {
          idSigafi: '1804567890',
          nombre: 'AMBOS',
          email: null,
          tipoUsuario: 'profesor',
          roles: [Roles.docente, Roles.inspector],
        },
      });

      expect(service.esDocente()).toBe(true);
      expect(service.esInspector()).toBe(true);
    });

    it('tieneRol devuelve true solo para los roles del usuario', () => {
      service.hydrateFromStorage({
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

      expect(service.tieneRol(Roles.docente)).toBe(true);
      expect(service.tieneRol(Roles.inspector)).toBe(false);
    });
  });
});
