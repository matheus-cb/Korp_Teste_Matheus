import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/**
 * Botão de atualizar.
 *
 * Existia repetido em quatro telas, e nas quatro sem retorno nenhum: clicar não
 * mudava o botão, então não dava para saber se a ação pegou — e clicar de novo
 * disparava outra requisição. Aqui ele gira enquanto carrega e fica travado,
 * que resolve as duas coisas de uma vez.
 */
@Component({
  selector: 'app-refresh-button',
  imports: [MatIconModule],
  template: `
    <button
      type="button"
      class="nf-btn nf-btn--icon"
      [attr.aria-label]="loading() ? 'Atualizando' : 'Atualizar'"
      [attr.aria-busy]="loading()"
      [disabled]="loading()"
      [title]="loading() ? 'Atualizando…' : 'Atualizar'"
      (click)="refresh.emit()"
    >
      <mat-icon svgIcon="refresh-cw" [class.spinning]="loading()" />
    </button>
  `,
  styles: `
    .spinning {
      animation: girar 900ms linear infinite;
    }

    /* Quem pediu menos movimento vê o botão desabilitado, sem a rotação. */
    @media (prefers-reduced-motion: reduce) {
      .spinning {
        animation: none;
      }
    }

    @keyframes girar {
      to {
        transform: rotate(360deg);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RefreshButtonComponent {
  readonly loading = input(false);
  readonly refresh = output<void>();
}
