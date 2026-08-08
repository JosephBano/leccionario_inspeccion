import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';

import { AuthService } from '@core/auth/services/auth.service';
import { Roles } from '@core/auth/models/roles';
import { BrandLogo } from '@shared/brand/brand-logo';

interface NavItem {
  label: string;
  path: string;
  icon: string;
  roles: readonly string[];
}

const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Mi horario', path: '/mi-horario', icon: 'calendar_month', roles: [Roles.docente] },
  { label: 'Mis paralelos', path: '/mis-paralelos', icon: 'list_alt', roles: [Roles.docente] },
  { label: 'Horarios', path: '/horarios', icon: 'schedule', roles: [Roles.inspector] },
  { label: 'Franjas', path: '/franjas', icon: 'more_time', roles: [Roles.inspector] },
  { label: 'Reportes', path: '/reportes', icon: 'analytics', roles: [Roles.inspector] },
];

@Component({
  selector: 'app-main-layout',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatListModule,
    BrandLogo,
  ],
  template: `
    <div class="shell">
      <mat-toolbar class="topbar">
        <app-brand-logo [onDark]="true" />
        <span class="spacer"></span>
        @if (auth.isAuthenticated()) {
          <button mat-button [matMenuTriggerFor]="userMenu" class="user-button">
            <mat-icon>account_circle</mat-icon>
            <span class="user-name">{{ auth.usuario()?.nombre }}</span>
          </button>
          <mat-menu #userMenu="matMenu">
            <button mat-menu-item (click)="logout()">
              <mat-icon>logout</mat-icon>
              <span>Cerrar sesión</span>
            </button>
          </mat-menu>
        }
      </mat-toolbar>

      <div class="body">
        <nav class="sidebar" aria-label="Navegación principal">
          <mat-nav-list>
            @for (item of visibleNav(); track item.path) {
              <a mat-list-item [routerLink]="item.path" routerLinkActive="active">
                <mat-icon matListItemIcon>{{ item.icon }}</mat-icon>
                <span matListItemTitle>{{ item.label }}</span>
              </a>
            }
          </mat-nav-list>
        </nav>

        <main class="content">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
  styles: [`
    .shell { display: flex; flex-direction: column; min-height: 100vh; }
    .topbar {
      position: sticky;
      top: 0;
      z-index: 10;
      gap: 12px;
      background: var(--cplec-navy);
      color: #fff;
      box-shadow: inset 0 -3px 0 var(--cplec-gold);
    }
    .spacer { flex: 1; }
    .user-button { display: inline-flex; align-items: center; gap: 8px; color: #fff; }
    .user-name { font-weight: 600; font-family: var(--cplec-font-body); }
    .body { display: grid; grid-template-columns: 248px 1fr; flex: 1; }
    .sidebar { background: #fff; border-right: 1px solid #e3e7ef; padding-top: 12px; }
    .content { padding: 24px; max-width: 1200px; margin: 0 auto; width: 100%; }
    .active {
      background: var(--cplec-gold-light);
      border-left: 3px solid var(--cplec-gold);
      font-weight: 600;
    }
    @media (max-width: 720px) {
      .body { grid-template-columns: 1fr; }
      .sidebar { border-right: none; border-bottom: 1px solid #e3e7ef; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MainLayout {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly visibleNav = computed<readonly NavItem[]>(() => {
    const roles = this.auth.usuario()?.roles ?? [];
    return NAV_ITEMS.filter((item) => item.roles.some((rol) => roles.includes(rol)));
  });

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
