import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { AuthService } from '@core/auth/services/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
  styles: [`
    :host { display: block; min-height: 100vh; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  // Mantenemos la referencia para que el hydrate del AuthService corra al bootstrap.
  protected readonly auth = inject(AuthService);
}
