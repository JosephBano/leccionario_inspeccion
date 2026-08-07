import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-unauthorized-page',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  template: `
    <section class="empty">
      <mat-icon aria-hidden="true">lock</mat-icon>
      <h2>Sin acceso</h2>
      <p>Tu rol actual no tiene permisos para esta sección.</p>
      <a mat-stroked-button color="primary" routerLink="/">Volver al inicio</a>
    </section>
  `,
  styles: [`
    .empty {
      display: flex; flex-direction: column; align-items: center; gap: 12px;
      padding: 48px 16px; text-align: center;
    }
    mat-icon { font-size: 48px; height: 48px; width: 48px; color: #de3c35; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UnauthorizedPage {}
