import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MAT_SNACK_BAR_DATA, MatSnackBarRef } from '@angular/material/snack-bar';

export type ToastTone = 'success' | 'error' | 'warning' | 'info';

export interface ToastData {
  tone: ToastTone;
  title: string;
  message?: string;
  /** Milissegundos; alimenta a barra de progresso. */
  duration: number;
}

const ICONS: Record<ToastTone, string> = {
  success: 'circle-check',
  error: 'circle-alert',
  warning: 'triangle-alert',
  info: 'info',
};

const TITLES: Record<ToastTone, string> = {
  success: 'Sucesso',
  error: 'Erro',
  warning: 'Atenção',
  info: 'Informação',
};

/**
 * Toast padrão da aplicação: superfície escura sobre a interface clara, ícone
 * e barra de progresso na cor do tom. A barra existe para o operador saber
 * quanto tempo tem antes de a mensagem sumir.
 */
@Component({
  selector: 'app-toast',
  imports: [MatIconModule],
  template: `
    <div class="toast" [class]="'toast--' + data.tone" role="status" aria-live="polite">
      <mat-icon class="icon" [svgIcon]="icon" aria-hidden="true" />

      <div class="body">
        <strong class="title">{{ data.title || defaultTitle }}</strong>
        @if (data.message) {
          <p class="message">{{ data.message }}</p>
        }
      </div>

      <button type="button" class="close" aria-label="Fechar aviso" (click)="dismiss()">
        <mat-icon svgIcon="x" />
      </button>

      <span class="progress" [style.animation-duration.ms]="data.duration"></span>
    </div>
  `,
  styles: `
    .toast {
      --tone: var(--n-400);

      position: relative;
      display: flex;
      overflow: hidden;
      min-width: 300px;
      max-width: 440px;
      align-items: flex-start;
      padding: var(--sp-3) var(--sp-3) var(--sp-3) var(--sp-4);
      border-radius: var(--r-md);
      background: var(--chrome-900);
      box-shadow: 0 10px 30px rgb(20 26 32 / 30%);
      gap: var(--sp-3);
    }

    .toast--success {
      --tone: #2f9e5f;
    }

    .toast--error {
      --tone: #e0574a;
    }

    .toast--warning {
      --tone: #e0a52a;
    }

    .toast--info {
      --tone: #3f8fdd;
    }

    .icon {
      width: 20px;
      height: 20px;
      flex: none;
      margin-top: 1px;
      color: var(--tone);
      font-size: 20px;
    }

    .icon svg,
    .close svg {
      display: block;
      width: 100%;
      height: 100%;
    }

    .body {
      min-width: 0;
      flex: 1;
    }

    .title {
      display: block;
      color: #ffffff;
      font-size: var(--fs-md);
      font-weight: 640;
      line-height: 1.3;
    }

    .message {
      margin: 3px 0 0;
      color: #c5cdd5;
      font-size: var(--fs-sm);
      line-height: 1.45;
    }

    .close {
      display: grid;
      width: 22px;
      height: 22px;
      flex: none;
      border: 0;
      border-radius: var(--r-sm);
      color: #95a0aa;
      background: transparent;
      cursor: pointer;
      place-items: center;
    }

    .close:hover {
      color: #ffffff;
      background: rgb(255 255 255 / 12%);
    }

    .close mat-icon {
      width: 14px;
      height: 14px;
      font-size: 14px;
    }

    .progress {
      position: absolute;
      bottom: 0;
      left: 0;
      width: 100%;
      height: 3px;
      background: var(--tone);
      transform-origin: left center;
      animation-name: drain;
      animation-timing-function: linear;
      animation-fill-mode: forwards;
    }

    @keyframes drain {
      from {
        transform: scaleX(1);
      }

      to {
        transform: scaleX(0);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .progress {
        animation: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class ToastComponent {
  readonly data = inject<ToastData>(MAT_SNACK_BAR_DATA);
  private readonly ref = inject<MatSnackBarRef<ToastComponent>>(MatSnackBarRef);

  get icon(): string {
    return ICONS[this.data.tone];
  }

  get defaultTitle(): string {
    return TITLES[this.data.tone];
  }

  dismiss(): void {
    this.ref.dismiss();
  }
}
