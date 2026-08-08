import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, firstValueFrom, throwError } from 'rxjs';
import type { Observable } from 'rxjs';

import { environment } from '@env/environment';
import type { ApiError } from '@core/models/api-error.model';
import { ErrorCodes } from '@core/models/api-error.model';
import type { MiPerfilDto, UsuarioDto } from '@core/models/usuario.model';
import type { Role } from '@core/auth/models/roles';
import { Roles, isRole } from '@core/auth/models/roles';
import type {
  LoginRequest,
  LoginResponse,
  LogoutRequest,
  RefreshRequest,
  RefreshResponse,
} from '@core/auth/models/auth.models';

/**
 * Sesión persistida en localStorage (solo el refresh + datos del usuario).
 * El access token NUNCA toca storage — vive en memoria (`accessToken` signal).
 *
 * Razón (`docs/03 sección 7`): menor superficie XSS. Si el backend migra a cookie
 * `HttpOnly`, este servicio cambia y se borra localStorage.
 */
const REFRESH_KEY = 'cplec.refresh';
const USER_KEY = 'cplec.usuario';

interface StoredUsuario {
  idSigafi: string;
  nombre: string;
  email: string | null;
  tipoUsuario: string;
  roles: string[];
}

function safeParse<T>(raw: string | null): T | null {
  if (!raw) return null;
  try {
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  // --- Estado en memoria ---------------------------------------------------
  private readonly _accessToken = signal<string | null>(null);
  private readonly _refreshToken = signal<string | null>(null);
  private readonly _usuario = signal<UsuarioDto | null>(null);
  private readonly _perfil = signal<MiPerfilDto | null>(null);

  readonly accessToken = this._accessToken.asReadonly();
  readonly refreshToken = this._refreshToken.asReadonly();
  readonly usuario = this._usuario.asReadonly();
  readonly perfil = this._perfil.asReadonly();
  readonly isAuthenticated = computed(() => this._accessToken() !== null);
  readonly hasSession = computed(() => this._refreshToken() !== null);
  readonly esDocente = computed(() => this.tieneRol(Roles.docente));
  readonly esInspector = computed(() => this.tieneRol(Roles.inspector));
  readonly rolPrincipal = computed<Role | null>(() => {
    const u = this._usuario();
    if (!u) return null;
    const found = u.roles.find(isRole);
    return found ?? null;
  });

  constructor() {
    this.hydrateFromStorage({});
  }

  /**
   * Llamado por el interceptor / app initializer. Si encuentra refresh
   * token válido en storage y un usuario guardado, restaura la sesión.
   */
  hydrateFromStorage(init?: {
    access?: string;
    refresh?: string;
    usuario?: StoredUsuario;
  }): void {
    const refresh = init?.refresh ?? localStorage.getItem(REFRESH_KEY);
    const usuario = init?.usuario ?? safeParse<StoredUsuario>(localStorage.getItem(USER_KEY));
    const access = init?.access ?? null;

    if (refresh && usuario) {
      this._refreshToken.set(refresh);
      this._usuario.set(usuario);
      if (access) this._accessToken.set(access);
    } else if (!refresh) {
      this.clearLocal();
    }
  }

  tieneRol(rol: Role): boolean {
    return this._usuario()?.roles.includes(rol) ?? false;
  }

  // --- Login / refresh / logout -------------------------------------------
  async login(credenciales: LoginRequest): Promise<LoginResponse> {
    const response = await firstValueFrom(
      this.http
        .post<LoginResponse>(`${environment.apiUrl}/auth/login`, credenciales)
        .pipe(catchError((err) => this.mapAndRethrow(err))),
    );
    this.applyLoginResponse(response);
    return response;
  }

  async refresh(): Promise<RefreshResponse> {
    const current = this._refreshToken();
    if (!current) {
      return Promise.reject(new Error('No hay refresh token guardado.'));
    }

    const body: RefreshRequest = { refreshToken: current };
    return firstValueFrom(
      this.http
        .post<RefreshResponse>(`${environment.apiUrl}/auth/refresh`, body)
        .pipe(
          catchError((err) => {
            this.clearLocal();
            return throwError(() => err);
          }),
        ),
    ).then((res) => {
      this._accessToken.set(res.accessToken);
      this._refreshToken.set(res.refreshToken);
      localStorage.setItem(REFRESH_KEY, res.refreshToken);
      return res;
    });
  }

  async logout(): Promise<void> {
    const current = this._refreshToken();
    if (current) {
      const body: LogoutRequest = { refreshToken: current };
      try {
        await firstValueFrom(
          this.http.post(`${environment.apiUrl}/auth/logout`, body, {
            responseType: 'text',
          }),
        );
      } catch {
        // Aunque el backend falle (5xx, red), limpiamos local.
      }
    }
    this.clearLocal();
    await this.router.navigate(['/login']);
  }

  async cargarMiPerfil(): Promise<MiPerfilDto> {
    const perfil = await firstValueFrom(
      this.http
        .get<MiPerfilDto>(`${environment.apiUrl}/auth/me`)
        .pipe(catchError((err) => this.mapAndRethrow(err))),
    );
    this._perfil.set(perfil);
    return perfil;
  }

  // --- Helpers -------------------------------------------------------------
  private applyLoginResponse(response: LoginResponse): void {
    this._accessToken.set(response.accessToken);
    this._refreshToken.set(response.refreshToken);
    this._usuario.set(response.usuario);
    localStorage.setItem(REFRESH_KEY, response.refreshToken);
    localStorage.setItem(USER_KEY, JSON.stringify(response.usuario));
  }

  private clearLocal(): void {
    this._accessToken.set(null);
    this._refreshToken.set(null);
    this._usuario.set(null);
    this._perfil.set(null);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
  }

  private mapAndRethrow(err: HttpErrorResponse): Observable<never> {
    const apiError: ApiError = (err.error as ApiError | null) ?? {
      codigo: ErrorCodes.ERROR_INTERNO,
      mensaje: err.message || 'Error inesperado',
      detalles: null,
      traceId: err.headers?.get('traceparent') ?? '',
    };
    const wrapped = new HttpErrorResponse({
      headers: err.headers,
      status: err.status,
      statusText: err.statusText,
      url: err.url ?? undefined,
      error: apiError,
    });
    (wrapped as HttpErrorResponse & { codigo?: string }).codigo = apiError.codigo;
    return throwError(() => wrapped);
  }
}
