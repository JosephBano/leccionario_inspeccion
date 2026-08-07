import type { UsuarioDto } from '@core/models/usuario.model';

/**
 * Respuesta de `POST /api/auth/login` y `POST /api/auth/refresh`.
 * En refresh el campo `usuario` NO viene; el frontend conserva el anterior.
 */
export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  usuario: UsuarioDto;
}

export interface RefreshResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface RefreshRequest {
  refreshToken: string;
}

export interface LogoutRequest {
  refreshToken: string;
}
