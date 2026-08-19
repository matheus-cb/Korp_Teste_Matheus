import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';

export type AlertTone = 'error' | 'warning' | 'info' | 'success';

@Component({
  selector: 'app-inline-alert',
  imports: [MatButtonModule],
  template: `
    <section
      class="alert"
      [class]="'alert ' + tone()"
      [attr.role]="tone() === 'error' ? 'alert' : 'status'"
    >
      <span class="indicator" aria-hidden="true">{{ symbol }}</span>
      <div>
        <strong>{{ title() }}</strong>
        <p>{{ message() }}</p>
        @if (traceId()) {
          <small>Referência: {{ traceId() }}</small>
        }
      </div>
      @if (retryable()) {
        <button mat-button type="button" (click)="retry.emit()">Tentar novamente</button>
      }
    </section>
  `,
  styles: `
    .alert {
      display: grid;
      grid-template-columns: auto 1fr auto;
      gap: 0.8rem;
      align-items: start;
      padding: 0.95rem 1rem;
      border: 1px solid;
      border-radius: 0.85rem;
      background: white;
    }
    .indicator {
      display: grid;
      width: 26px;
      height: 26px;
      border-radius: 999px;
      place-items: center;
      font-size: 0.78rem;
      font-weight: 900;
    }
    strong {
      display: block;
      color: #28453f;
      font-size: 0.86rem;
    }
    p {
      margin: 0.2rem 0 0;
      color: #61746f;
      font-size: 0.82rem;
      line-height: 1.45;
    }
    small {
      display: block;
      margin-top: 0.32rem;
      color: #82938f;
      font-family: monospace;
      font-size: 0.68rem;
    }
    .error {
      border-color: #f1c5c0;
      background: #fff8f7;
    }
    .error .indicator {
      color: #9a3229;
      background: #fbe0dc;
    }
    .warning {
      border-color: #eed7a8;
      background: #fffbf2;
    }
    .warning .indicator {
      color: #8b5b00;
      background: #f9e7bc;
    }
    .info {
      border-color: #bcd9e6;
      background: #f5fbfe;
    }
    .info .indicator {
      color: #176783;
      background: #dceff7;
    }
    .success {
      border-color: #b9dfd2;
      background: #f4fbf8;
    }
    .success .indicator {
      color: #117056;
      background: #d9f1e9;
    }
    @media (width <= 520px) {
      .alert {
        grid-template-columns: auto 1fr;
      }
      button {
        grid-column: 2;
        justify-self: start;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class InlineAlertComponent {
  readonly tone = input<AlertTone>('info');
  readonly title = input.required<string>();
  readonly message = input.required<string>();
  readonly traceId = input<string>();
  readonly retryable = input(false);
  readonly retry = output<void>();

  get symbol(): string {
    return { error: '!', warning: '!', info: 'i', success: '✓' }[this.tone()];
  }
}
