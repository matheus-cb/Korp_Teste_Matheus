import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { ClosureAttempt, InvoiceStatus } from '../core/models/invoice.model';

/**
 * O estado que o operador precisa ver não é o `status` cru: uma nota continua
 * `Open` enquanto o fechamento está pendente ou foi rejeitado, e é justamente
 * essa diferença que torna a saga visível na tela.
 */
export type DisplayState = 'Open' | 'Closed' | 'Pending' | 'Rejected';

export function toDisplayState(
  status: InvoiceStatus,
  closure?: ClosureAttempt | null,
): DisplayState {
  if (status === 'Closed') return 'Closed';
  if (closure?.state === 'Pending') return 'Pending';
  if (closure?.state === 'Rejected') return 'Rejected';
  return 'Open';
}

const LABELS: Record<DisplayState, string> = {
  Open: 'Aberta',
  Closed: 'Fechada',
  Pending: 'Pendente',
  Rejected: 'Rejeitada',
};

/** Cor só onde há ação pendente; concluída é neutra. */
const CLASSES: Record<DisplayState, string> = {
  Open: 'status-open',
  Closed: 'status-done',
  Pending: 'status-pending',
  Rejected: 'status-rejected',
};

@Component({
  selector: 'app-status-pill',
  template: `
    <span class="status-badge" [class]="CLASSES[state()]">{{ LABELS[state()] }}</span>
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class StatusPillComponent {
  readonly state = input.required<DisplayState>();
  protected readonly LABELS = LABELS;
  protected readonly CLASSES = CLASSES;
}
