import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/**
 * Casca padrão de modal do NotaFlow: cabeçalho com título e fechar, corpo
 * delimitado e rodapé com duas ações dividindo a largura. Toda ação de
 * criação da aplicação usa esta casca, para que nenhum modal invente layout.
 */
@Component({
  selector: 'app-modal-shell',
  imports: [MatIconModule],
  template: `
    <section class="modal">
      <header class="modal-head">
        <h2>{{ title() }}</h2>
        <button
          type="button"
          class="close"
          aria-label="Fechar"
          [disabled]="busy()"
          (click)="dismiss.emit()"
        >
          <mat-icon svgIcon="x" />
        </button>
      </header>

      <div class="modal-body">
        <ng-content />
      </div>

      <footer class="modal-foot">
        <button type="button" class="foot-btn" [disabled]="busy()" (click)="dismiss.emit()">
          {{ dismissLabel() }}
        </button>
        <button
          type="button"
          class="foot-btn foot-btn--primary"
          [disabled]="busy() || !canConfirm()"
          (click)="confirm.emit()"
        >
          {{ busy() ? busyLabel() : confirmLabel() }}
        </button>
      </footer>
    </section>
  `,
  styles: `
    .modal {
      display: flex;
      max-height: min(82vh, 760px);
      flex-direction: column;
    }

    @media (width <= 600px) {
      /* Em tela estreita o rodapé empilha, senão os dois botões ficam ilegíveis. */
      .modal-foot {
        grid-template-columns: 1fr;
      }

      .foot-btn {
        border-right: 0;
        border-top: 1px solid var(--n-200);
      }
    }

    .modal-head {
      display: flex;
      align-items: center;
      padding: var(--sp-3) var(--sp-3) var(--sp-3) var(--sp-4);
      border-bottom: 1px solid var(--n-200);
      background: var(--n-50);
      gap: var(--sp-3);
    }

    .modal-head h2 {
      margin: 0;
      color: var(--n-900);
      font-size: var(--fs-lg);
      font-weight: 640;
    }

    .close {
      display: grid;
      width: 28px;
      height: 28px;
      border: 1px solid var(--n-300);
      border-radius: var(--r-sm);
      color: var(--n-500);
      background: var(--n-0);
      cursor: pointer;
      margin-left: auto;
      place-items: center;
    }

    .close:hover:not(:disabled) {
      color: var(--n-700);
      background: var(--n-100);
    }

    .close mat-icon {
      width: 15px;
      height: 15px;
      font-size: 15px;
    }

    .close mat-icon svg {
      display: block;
      width: 100%;
      height: 100%;
    }

    /* Corpo delimitado, como no wireframe. */
    .modal-body {
      overflow-y: auto;
      flex: 1;
      padding: var(--sp-4);
      background: var(--n-0);
    }

    /* Rodapé com as duas ações dividindo a largura em partes iguais. */
    .modal-foot {
      display: grid;
      border-top: 1px solid var(--n-200);
      background: var(--n-25);
      grid-template-columns: 1fr 1fr;
    }

    .foot-btn {
      height: 44px;
      border: 0;
      border-right: 1px solid var(--n-200);
      color: var(--n-600);
      background: transparent;
      font-family: inherit;
      font-size: var(--fs-md);
      font-weight: 600;
      cursor: pointer;
    }

    .foot-btn:hover:not(:disabled) {
      background: var(--n-100);
    }

    .foot-btn:disabled {
      color: var(--n-400);
      cursor: not-allowed;
    }

    .foot-btn--primary {
      border-right: 0;
      color: #ffffff;
      background: var(--brand-600);
    }

    .foot-btn--primary:hover:not(:disabled) {
      background: var(--brand-700);
    }

    .foot-btn--primary:disabled {
      color: #ffffff;
      opacity: 0.65;
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class ModalShellComponent {
  readonly title = input.required<string>();
  readonly confirmLabel = input('Salvar');
  readonly dismissLabel = input('Sair');
  readonly busyLabel = input('Salvando…');
  readonly busy = input(false);
  readonly canConfirm = input(true);

  readonly confirm = output<void>();
  readonly dismiss = output<void>();
}
