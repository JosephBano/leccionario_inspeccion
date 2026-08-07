import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { BrandLogo } from '@shared/brand/brand-logo';

@Component({
  selector: 'app-auth-layout',
  imports: [RouterOutlet, BrandLogo],
  template: `
    <main class="auth-shell">
      <div class="auth-card">
        <header class="auth-header">
          <app-brand-logo [tagline]="true" />
          <p class="auth-subtitle">Escuela de Conducción Profesional</p>
        </header>
        <router-outlet />
      </div>
    </main>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; }
    .auth-shell {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      padding: 24px;
      background:
        radial-gradient(circle at 0% 0%, rgba(34, 44, 87, 0.06), transparent 50%),
        radial-gradient(circle at 100% 100%, rgba(196, 168, 87, 0.10), transparent 50%),
        var(--cplec-cream);
    }
    .auth-card {
      width: 100%;
      max-width: 420px;
      background: #fff;
      border-top: 3px solid var(--cplec-gold);
      border-radius: var(--cplec-radius-lg, 12px);
      padding: 32px 28px;
      box-shadow: 0 4px 16px rgba(18, 23, 48, 0.1);
    }
    .auth-header { text-align: center; margin-bottom: 24px; }
    .auth-subtitle {
      margin: 10px 0 0;
      color: var(--cplec-ink-muted);
      font-size: 0.92rem;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthLayout {}
