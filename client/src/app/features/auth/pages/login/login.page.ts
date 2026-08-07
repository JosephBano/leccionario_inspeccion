import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import type { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { AuthService } from '@core/auth/services/auth.service';
import { ErrorCodes } from '@core/models/api-error.model';

interface LoginError {
  codigo: string;
  mensaje: string;
}

/**
 * Mapea el codigo de error de la API a un mensaje amigable para el usuario.
 * Los mensajes del backend pueden cambiar — el cliente decide por codigo.
 */
function mensajeParaUsuario(codigo: string): string {
  switch (codigo) {
    case ErrorCodes.CREDENCIALES_INVALIDAS:
      return 'Cédula o contraseña incorrectas.';
    case ErrorCodes.CUENTA_INACTIVA:
      return 'Tu cuenta está inactiva. Contacta al administrador.';
    case ErrorCodes.SIN_ACCESO_SISTEMA:
      return 'No tienes acceso al sistema de leccionario. Solicita el rol a tu coordinador.';
    case ErrorCodes.DEMASIADAS_PETICIONES:
      return 'Demasiados intentos. Espera unos minutos e intenta de nuevo.';
    default:
      return 'No se pudo iniciar sesión. Intenta de nuevo.';
  }
}

@Component({
  selector: 'app-login-page',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <form [formGroup]="form" (ngSubmit)="onSubmit()" class="form">
      <mat-form-field appearance="outline" class="full">
        <mat-label>Cédula</mat-label>
        <input
          matInput
          type="text"
          inputmode="numeric"
          autocomplete="username"
          maxlength="20"
          formControlName="username"
          required
        />
        @if (form.controls.username.touched && form.controls.username.invalid) {
          <mat-error>Ingresa tu cédula.</mat-error>
        }
      </mat-form-field>

      <mat-form-field appearance="outline" class="full">
        <mat-label>Contraseña</mat-label>
        <input
          matInput
          [type]="showPassword() ? 'text' : 'password'"
          autocomplete="current-password"
          formControlName="password"
          required
        />
        <button
          mat-icon-button
          matSuffix
          type="button"
          aria-label="Mostrar u ocultar contraseña"
          (click)="togglePassword()"
        >
          <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
        </button>
        @if (form.controls.password.touched && form.controls.password.invalid) {
          <mat-error>Ingresa tu contraseña.</mat-error>
        }
      </mat-form-field>

      @if (error(); as e) {
        <div class="error" role="alert">
          <mat-icon aria-hidden="true">error_outline</mat-icon>
          <span>{{ e.mensaje }}</span>
        </div>
      }

      <button
        mat-flat-button
        color="primary"
        type="submit"
        class="full submit"
        [disabled]="loading() || form.invalid"
      >
        @if (loading()) {
          <mat-spinner diameter="20"></mat-spinner>
          <span>Ingresando…</span>
        } @else {
          <span>Ingresar</span>
        }
      </button>
    </form>
  `,
  styles: [`
    .form { display: flex; flex-direction: column; gap: 8px; }
    .full { width: 100%; }
    .submit { margin-top: 8px; min-height: 44px; }
    .error {
      display: flex; align-items: center; gap: 8px;
      background: var(--cplec-color-ausente-bg, #fdecea);
      color: var(--cplec-color-ausente, #c5211f);
      padding: 10px 12px; border-radius: 8px; margin-top: 4px;
      font-size: 0.9rem;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly form = this.fb.nonNullable.group({
    username: ['', [Validators.required, Validators.minLength(1)]],
    password: ['', [Validators.required, Validators.minLength(1)]],
  });

  readonly loading = signal(false);
  readonly showPassword = signal(false);
  readonly error = signal<LoginError | null>(null);

  protected togglePassword(): void {
    this.showPassword.update((v) => !v);
  }

  async onSubmit(): Promise<void> {
    if (this.form.invalid || this.loading()) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    try {
      await this.auth.login(this.form.getRawValue());
      const redirect = this.route.snapshot.queryParamMap.get('redirectTo');
      await this.router.navigateByUrl(redirect ?? '/');
    } catch (err: unknown) {
      const codigo = (err as HttpErrorResponse & { codigo?: string })?.codigo
        ?? ErrorCodes.ERROR_INTERNO;
      this.error.set({ codigo, mensaje: mensajeParaUsuario(codigo) });
    } finally {
      this.loading.set(false);
    }
  }
}
