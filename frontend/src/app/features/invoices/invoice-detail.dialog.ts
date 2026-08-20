import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import type { OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { filter, finalize, switchMap, take, tap, timeout, timer } from 'rxjs';
import type { UiError } from '../../core/models/api.models';
import type { Invoice } from '../../core/models/invoice.model';
import { ApiErrorService } from '../../core/services/api-error.service';
import { ExportService } from '../../core/services/export.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { NotificationService } from '../../core/services/notification.service';
import { InlineAlertComponent } from '../../shared/inline-alert.component';
import { LoadingStateComponent } from '../../shared/loading-state.component';
import { ModalShellComponent } from '../../shared/modal-shell.component';
import { CreateInvoiceDialog } from './create-invoice.dialog';
import { StatusPillComponent, toDisplayState } from '../../shared/status-pill.component';

/**
 * Detalhe da nota em modal, sobre a listagem. Concentra a ação crítica
 * (Imprimir e fechar), a reconciliação por polling e a segunda via do PDF.
 */
@Component({
  selector: 'app-invoice-detail-dialog',
  imports: [
    DatePipe,
    InlineAlertComponent,
    LoadingStateComponent,
    MatIconModule,
    MatProgressSpinnerModule,
    ModalShellComponent,
    StatusPillComponent,
  ],
  template: `
    <app-modal-shell
      [title]="invoice ? 'Nota fiscal #' + invoice.number : 'Nota fiscal'"
      [confirmLabel]="confirmLabel()"
      dismissLabel="Sair"
      [busyLabel]="closing ? 'Fechando…' : 'Verificando…'"
      [busy]="closing || polling"
      [canConfirm]="!!invoice"
      (confirm)="primaryAction()"
      (dismiss)="close()"
    >
      @if (loading) {
        <app-loading-state message="Carregando a nota…" />
      } @else if (loadError) {
        <app-inline-alert
          tone="error"
          [title]="loadError.title"
          [message]="loadError.message"
          [traceId]="loadError.traceId"
          [retryable]="true"
          (retry)="load()"
        />
      } @else if (invoice) {
        <header class="head">
          <app-status-pill [state]="toDisplayState(invoice.status, invoice.closure)" />
          <span class="note">Documento demonstrativo · sem validade fiscal</span>
          <div class="head-actions">
            @if (invoice.status === 'Open' && invoice.closure?.state !== 'Pending') {
              <button type="button" class="nf-btn" (click)="editInvoice()"><mat-icon svgIcon="pencil" />Editar</button>
            }
            <button type="button" class="nf-btn" (click)="exportCsv()">
              <mat-icon svgIcon="download" />
              Excel
            </button>
            <button
              type="button"
              class="nf-btn"
              [disabled]="invoice.status !== 'Closed' || downloadingPdf"
              (click)="downloadPdf()"
            >
              @if (downloadingPdf) {
                <mat-spinner class="button-spinner" diameter="16" />
              } @else {
                <mat-icon svgIcon="printer" />
              }
              PDF
            </button>
          </div>
        </header>

        @if (confirming) {
          <section class="confirm" role="alertdialog">
            <strong>Imprimir e fechar a nota #{{ invoice.number }}?</strong>
            <p>
              O Estoque validará e descontará todos os itens em uma única operação.
              Depois de fechada, a nota não poderá ser alterada.
            </p>
            <div class="confirm-actions">
              <button type="button" class="nf-btn" (click)="confirming = false">Cancelar</button>
              <button type="button" class="nf-btn nf-btn--primary" (click)="requestClose()">
                Confirmar fechamento
              </button>
            </div>
          </section>
        }

        @if (operationError) {
          <app-inline-alert
            tone="error"
            [title]="operationError.title"
            [message]="operationError.message"
            [traceId]="operationError.traceId"
          />
        }

        @if (polling || invoice.closure?.state === 'Pending') {
          <section class="banner pending" role="status" aria-live="polite">
            <mat-spinner diameter="20" />
            <div>
              <strong>Verificando o resultado do fechamento</strong>
              <p>
                Consultamos a mesma tentativa para evitar qualquer baixa duplicada.
              </p>
            </div>
          </section>
        }

        @if (invoice.closure?.ignoredItems?.length) {
          <section class="ignored">
            <strong>Itens sem movimentação de estoque</strong>
            <p>
              Entraram na nota, mas não consumiram saldo porque o produto não
              controla estoque.
            </p>
            <ul>
              @for (item of invoice.closure!.ignoredItems!; track item.productId) {
                <li>
                  <span class="code">{{ item.code }}</span>
                  <span>{{ item.quantity }} un.</span>
                </li>
              }
            </ul>
          </section>
        }

        <dl class="facts">
          <div>
            <dt>Emissão</dt>
            <dd>{{ invoice.createdAt | date: 'dd/MM/yyyy HH:mm' }}</dd>
          </div>
          <div>
            <dt>Fechamento</dt>
            <dd>{{ invoice.closedAt ? (invoice.closedAt | date: 'dd/MM/yyyy HH:mm') : '—' }}</dd>
          </div>
          <div>
            <dt>Itens</dt>
            <dd>{{ invoice.items.length }}</dd>
          </div>
          <div>
            <dt>Unidades</dt>
            <dd>{{ totalUnits }}</dd>
          </div>
          <div>
            <dt>Emitida por</dt>
            <dd>{{ invoice.createdBy }}</dd>
          </div>
          <div>
            <dt>Fechada por</dt>
            <dd>{{ invoice.closedBy ?? '—' }}</dd>
          </div>
          <div>
            <dt>Última edição</dt>
            <dd>{{ invoice.updatedBy ?? invoice.createdBy }}</dd>
          </div>
        </dl>

        <table class="data-table items">
          <thead>
            <tr>
              <th scope="col">Código</th>
              <th scope="col">Produto</th>
              <th scope="col" class="numeric">Quantidade</th>
            </tr>
          </thead>
          <tbody>
            @for (item of invoice.items; track item.productId) {
              <tr>
                <td class="code">{{ item.code }}</td>
                <td>{{ item.description }}</td>
                <td class="numeric">{{ item.quantity }}</td>
              </tr>
            }
          </tbody>
          <tfoot>
            <tr>
              <td colspan="2">Total de unidades</td>
              <td class="numeric">{{ totalUnits }}</td>
            </tr>
          </tfoot>
        </table>

        <p class="snapshot">
          Código e descrição são um retrato do momento da emissão: mudanças no catálogo
          não alteram esta nota.
        </p>
        @if (invoice.auditEvents?.length) {
          <section class="history"><strong>Histórico</strong><ul>@for (event of invoice.auditEvents; track event.occurredAt + event.type) {<li>{{ event.type === 'Created' ? 'Criada' : event.type === 'Edited' ? 'Editada' : 'Fechada' }} por {{ event.actorName }} · {{ event.occurredAt | date: 'dd/MM/yyyy HH:mm' }}</li>}</ul></section>
        }
      }
    </app-modal-shell>
  `,
  styles: `
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      margin-bottom: var(--sp-4);
      gap: var(--sp-3);
    }

    .note {
      color: var(--n-500);
      font-size: var(--fs-sm);
    }

    .head-actions {
      display: flex;
      margin-left: auto;
      gap: var(--sp-2);
    }

    .confirm {
      margin-bottom: var(--sp-4);
      padding: var(--sp-3) var(--sp-4);
      border: 1px solid var(--st-pending-dot);
      border-radius: var(--r-md);
      background: var(--st-pending-bg);
    }

    .confirm strong {
      color: var(--st-pending-fg);
    }

    .confirm p {
      margin: var(--sp-1) 0 var(--sp-3);
      color: var(--n-700);
      font-size: var(--fs-sm);
    }

    .confirm-actions {
      display: flex;
      gap: var(--sp-2);
      justify-content: flex-end;
    }

    .banner {
      display: flex;
      align-items: center;
      margin-bottom: var(--sp-4);
      padding: var(--sp-3) var(--sp-4);
      border-radius: var(--r-md);
      background: var(--n-50);
      gap: var(--sp-3);
    }

    .banner p {
      margin: 2px 0 0;
      color: var(--n-500);
      font-size: var(--fs-sm);
    }

    .ignored {
      margin-bottom: var(--sp-4);
      padding: var(--sp-3) var(--sp-4);
      border: 1px solid var(--st-open-dot);
      border-radius: var(--r-md);
      background: var(--st-open-bg);
    }

    .ignored strong {
      color: var(--st-open-fg);
      font-size: var(--fs-md);
    }

    .ignored p {
      margin: var(--sp-1) 0 var(--sp-2);
      color: var(--n-700);
      font-size: var(--fs-sm);
    }

    .ignored ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .ignored li {
      display: flex;
      padding: 2px 0;
      font-size: var(--fs-sm);
      gap: var(--sp-3);
    }

    .facts {
      display: grid;
      margin: 0 0 var(--sp-4);
      border: 1px solid var(--n-200);
      border-radius: var(--r-md);
      grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
    }

    .facts > div {
      padding: var(--sp-3) var(--sp-4);
      border-left: 1px solid var(--n-100);
    }

    .facts > div:first-child {
      border-left: 0;
    }

    .facts dt {
      color: var(--n-500);
      font-size: var(--fs-xs);
      font-weight: 660;
      letter-spacing: 0.05em;
      text-transform: uppercase;
    }

    .facts dd {
      margin: 3px 0 0;
      color: var(--n-900);
      font-size: var(--fs-md);
      font-weight: 620;
      font-variant-numeric: tabular-nums;
    }

    .items {
      border: 1px solid var(--n-200);
    }

    .code {
      color: var(--n-600);
      font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
      font-size: var(--fs-sm);
    }

    .snapshot {
      margin: var(--sp-3) 0 0;
      color: var(--n-500);
      font-size: var(--fs-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class InvoiceDetailDialog implements OnInit {
  protected readonly toDisplayState = toDisplayState;
  private readonly dialogRef = inject<MatDialogRef<InvoiceDetailDialog>>(MatDialogRef);
  private readonly data = inject<{ id: string }>(MAT_DIALOG_DATA);
  private readonly invoiceService = inject(InvoiceService);
  private readonly apiError = inject(ApiErrorService);
  private readonly notification = inject(NotificationService);
  private readonly exporter = inject(ExportService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly datePipe = new DatePipe('pt-BR');
  private readonly dialog = inject(MatDialog);

  invoice: Invoice | null = null;
  loading = true;
  closing = false;
  polling = false;
  confirming = false;
  downloadingPdf = false;
  loadError: UiError | null = null;
  operationError: UiError | null = null;
  private downloadAfterClose = false;

  ngOnInit(): void {
    this.load();
  }

  get totalUnits(): number {
    return this.invoice?.items.reduce((total, item) => total + item.quantity, 0) ?? 0;
  }

  confirmLabel(): string {
    if (!this.invoice) return 'Fechar';
    return this.invoice.status === 'Closed' ? 'Baixar PDF' : 'Imprimir e fechar';
  }

  close(): void {
    if (!this.closing && !this.polling && !this.downloadingPdf) this.dialogRef.close();
  }

  primaryAction(): void {
    if (!this.invoice) return;
    if (this.invoice.status === 'Closed') {
      this.downloadPdf();
      return;
    }
    this.confirming = true;
  }

  load(): void {
    this.loading = true;
    this.loadError = null;
    this.invoiceService
      .getById(this.data.id)
      .pipe(
        finalize(() => (this.loading = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (invoice) => {
          this.invoice = invoice;
          if (invoice.status === 'Open' && invoice.closure?.state === 'Pending') {
            this.reconcile();
          }
        },
        error: (error: unknown) => (this.loadError = this.apiError.from(error)),
      });
  }

  requestClose(): void {
    if (!this.invoice || this.closing || this.polling) return;
    this.confirming = false;
    this.closing = true;
    // Os botões do modal já refletem esse estado, mas Esc e clique no fundo
    // passam direto pelo MatDialog. Não permita que a pessoa perca o retorno
    // enquanto a mesma tentativa idempotente está sendo confirmada.
    this.dialogRef.disableClose = true;
    this.downloadAfterClose = true;
    this.operationError = null;
    this.invoiceService
      .close(this.invoice.id)
      .pipe(
        finalize(() => (this.closing = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          if (result.invoice) this.invoice = result.invoice;
          if (result.state === 'Pending' || result.httpStatus === 202) {
            this.notification.info('Fechamento enviado', 'Estamos verificando o resultado junto ao Estoque.');
            this.reconcile();
            return;
          }
          this.notification.success('Nota fechada', 'O estoque foi atualizado e o PDF está disponível.');
          if (this.downloadAfterClose) {
            this.downloadAfterClose = false;
            this.downloadPdf(true);
          }
        },
        error: (error: unknown) => {
          this.dialogRef.disableClose = false;
          this.operationError = this.apiError.from(error);
        },
      });
  }

  /** Repete a consulta da MESMA tentativa até um estado terminal. */
  reconcile(): void {
    if (this.polling || !this.invoice) return;
    this.polling = true;
    this.dialogRef.disableClose = true;
    this.operationError = null;
    timer(0, 2000)
      .pipe(
        switchMap(() => this.invoiceService.getById(this.data.id)),
        tap((invoice) => (this.invoice = invoice)),
        filter(
          (invoice) => invoice.status === 'Closed' || invoice.closure?.state === 'Rejected',
        ),
        take(1),
        timeout({ first: 32_000 }),
        finalize(() => (this.polling = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (invoice) => {
          if (invoice.status === 'Closed') {
            this.notification.success('Nota fechada', 'Resultado confirmado pela reconciliação.');
            if (this.downloadAfterClose) {
              this.downloadAfterClose = false;
              this.downloadPdf(true);
            }
            return;
          }
          this.downloadAfterClose = false;
          this.dialogRef.disableClose = false;
          this.notification.warning(
            'Fechamento rejeitado',
            'Revise os itens da nota antes de tentar novamente.',
          );
        },
        error: (error: unknown) => {
          this.downloadAfterClose = false;
          this.dialogRef.disableClose = false;
          this.operationError = this.apiError.from(error);
        },
      });
  }

  editInvoice(): void {
    if (!this.invoice || this.invoice.status !== 'Open' || this.invoice.closure?.state === 'Pending') return;
    this.dialog.open(CreateInvoiceDialog, { width: 'var(--modal-md)', panelClass: 'nf-dialog', data: { invoice: this.invoice } })
      .afterClosed().pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id?: string) => { if (id) this.load(); });
  }

  /** Fecha o detalhe somente depois que o navegador recebeu o PDF inicial. */
  downloadPdf(closeDialogAfterSuccess = false): void {
    if (!this.invoice || this.downloadingPdf) return;
    this.downloadingPdf = true;
    this.operationError = null;
    const number = this.invoice.number;
    this.invoiceService
      .downloadPdf(this.invoice.id)
      .pipe(
        finalize(() => (this.downloadingPdf = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const anchor = document.createElement('a');
          anchor.href = url;
          anchor.download = `nota-${number}.pdf`;
          anchor.click();
          URL.revokeObjectURL(url);
          if (closeDialogAfterSuccess) this.dialogRef.close();
        },
        error: (error: unknown) => {
          // O fechamento da nota já é definitivo, mas o detalhe precisa ficar
          // disponível para a pessoa ver o erro e tentar baixar a segunda via.
          this.dialogRef.disableClose = false;
          this.operationError = this.apiError.from(error);
          this.notification.error('PDF indisponível', 'A nota está segura; apenas a geração do documento falhou.');
        },
      });
  }

  exportCsv(): void {
    if (!this.invoice) return;
    this.exporter.toCsv(
      this.invoice.items,
      [
        { header: 'Código', value: (item) => item.code },
        { header: 'Produto', value: (item) => item.description },
        { header: 'Quantidade', value: (item) => item.quantity },
      ],
      `nota-${this.invoice.number}`,
    );
  }
}
