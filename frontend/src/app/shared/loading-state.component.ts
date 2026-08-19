import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({
  selector: 'app-loading-state',
  imports: [MatProgressSpinnerModule],
  template: `
    <div class="loading" role="status" aria-live="polite">
      <mat-spinner diameter="28" />
      <span>{{ message() }}</span>
    </div>
  `,
  styles: `
    .loading {
      display: flex;
      gap: 0.8rem;
      align-items: center;
      justify-content: center;
      min-height: 180px;
      color: #647a74;
      font-size: 0.86rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class LoadingStateComponent {
  readonly message = input('Carregando dados…');
}
