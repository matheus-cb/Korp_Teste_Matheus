import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/**
 * Faixa do módulo: fica logo abaixo da faixa global do shell e muda a cada
 * tela. Só carrega o que pertence àquela tela — breadcrumb, título e ações.
 * O que persiste na aplicação inteira vive na faixa global do app-shell.
 */
@Component({
  selector: 'app-page-header',
  imports: [MatIconModule],
  template: `
    <header class="bar-module">
      <nav class="crumb" aria-label="Trilha de navegação">
        @if (module()) {
          <span class="crumb-parent">{{ module() }}</span>
          <mat-icon svgIcon="chevron-right" aria-hidden="true" />
        }
        <h1>{{ title() }}</h1>
      </nav>
      <div class="actions"><ng-content /></div>
    </header>
  `,
  styles: `
    .bar-module {
      position: sticky;
      z-index: 2;
      top: 0;
      display: flex;
      min-height: var(--bar-mod-h);
      align-items: center;
      padding: var(--sp-2) var(--sp-4);
      border-bottom: 1px solid var(--n-200);
      background: var(--n-0);
      gap: var(--sp-3);
    }

    .crumb {
      display: flex;
      min-width: 0;
      align-items: center;
      gap: 6px;
      color: var(--n-500);
      font-size: var(--fs-sm);
    }

    .crumb mat-icon {
      width: 14px;
      height: 14px;
      color: var(--n-300);
      font-size: 14px;
    }

    .crumb mat-icon svg {
      display: block;
      width: 100%;
      height: 100%;
    }

    .crumb-parent {
      white-space: nowrap;
    }

    h1 {
      overflow: hidden;
      margin: 0;
      color: var(--n-900);
      font-size: var(--fs-xl);
      font-weight: 600;
      letter-spacing: -0.01em;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      margin-left: auto;
      gap: var(--sp-2);
      justify-content: flex-end;
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class PageHeaderComponent {
  readonly module = input<string>('');
  readonly title = input.required<string>();
}
