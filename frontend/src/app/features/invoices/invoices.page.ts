import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router } from '@angular/router';
import type { ColDef, ValueFormatterParams } from 'ag-grid-community';
import { catchError, finalize, of } from 'rxjs';
import type { UiError } from '../../core/models/api.models';
import type { InvoiceStatus, InvoiceSummary } from '../../core/models/invoice.model';
import { ApiErrorService } from '../../core/services/api-error.service';
import { ExportService } from '../../core/services/export.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { DataGridComponent } from '../../shared/data-grid.component';
import { RefreshButtonComponent } from '../../shared/refresh-button.component';
import { InlineAlertComponent } from '../../shared/inline-alert.component';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { toDisplayState } from '../../shared/status-pill.component';
import { CreateInvoiceDialog } from './create-invoice.dialog';
import { ImportInvoicesDialog } from './import-invoices.dialog';
import { InvoiceDetailDialog } from './invoice-detail.dialog';
import { DataRefreshService } from '../../core/services/data-refresh.service';

type StatusFilter = 'all' | 'Open' | 'Closed';

const LABELS: Record<string, string> = {
  Open: 'Aberta',
  Closed: 'Fechada',
  Pending: 'Pendente',
  Rejected: 'Rejeitada',
};

const CLASSES: Record<string, string> = {
  Open: 'status-open',
  Closed: 'status-done',
  Pending: 'status-pending',
  Rejected: 'status-rejected',
};

@Component({
  selector: 'app-invoices-page',
  imports: [
    DataGridComponent,
    InlineAlertComponent,
    MatIconModule,
    PageHeaderComponent, RefreshButtonComponent],
  template: `
    <app-page-header module="Fiscal" title="Notas fiscais">
      <app-refresh-button [loading]="loading" (refresh)="load()" />
      <button type="button" class="nf-btn" (click)="openImport()">
        <mat-icon svgIcon="upload" />
        Importar
      </button>
      <button type="button" class="nf-btn" (click)="exportCsv()">
        <mat-icon svgIcon="download" />
        Exportar
      </button>
      <button type="button" class="nf-btn nf-btn--primary" (click)="openCreate()">
        <mat-icon svgIcon="plus" />
        Nova nota
      </button>
    </app-page-header>

    <section class="page-card">
      <div class="toolbar">
        <label class="search">
        <mat-icon svgIcon="search" aria-hidden="true" />
        <input
          #searchInput
          type="search"
          autocomplete="off"
          maxlength="100"
          placeholder="Número da nota"
          aria-label="Buscar pelo número da nota"
          (input)="applyQuery(searchInput.value)"
        />
      </label>

      <div class="seg" role="group" aria-label="Filtrar por situação">
        @for (option of statusOptions; track option.value) {
          <button
            type="button"
            [class.on]="statusFilter === option.value"
            [attr.aria-pressed]="statusFilter === option.value"
            (click)="setStatus(option.value)"
          >
            {{ option.label }}
          </button>
        }
      </div>

      <label class="period">
        <span class="sr-only">Período inicial</span>
        <input type="date" [value]="from" aria-label="De" (change)="setFrom($event)" />
      </label>
      <label class="period">
        <span class="sr-only">Período final</span>
        <input type="date" [value]="to" aria-label="Até" (change)="setTo($event)" />
      </label>

      </div>

      @if (error) {
        <div class="state-padding">
          <app-inline-alert
            tone="error"
            [title]="error.title"
            [message]="error.message"
            [traceId]="error.traceId"
            [retryable]="true"
            (retry)="load()"
          />
        </div>
      } @else {
        <div class="grid-area">
          <app-data-grid
            [rowData]="pageRows"
            [columnDefs]="columns"
            [pinnedBottomRowData]="totalRow"
            emptyMessage="Nenhuma nota encontrada"
            (rowClicked)="openDetail($event.id)"
          />
        </div>
      }

      <!-- Contagem a esquerda, paginacao a direita: o rodape do card responde
           "quantos" e "onde estou" sem competir com os filtros la em cima. -->
      <footer class="card-foot">
        <span>{{ filtered.length }} {{ filtered.length === 1 ? 'nota' : 'notas' }}</span>

        @if (!error) {
          <nav class="pager" aria-label="Paginação">
            <span>{{ rangeLabel() }}</span>
            <button
              type="button"
              class="nf-btn nf-btn--icon"
              aria-label="Página anterior"
              [disabled]="page === 1"
              (click)="goTo(page - 1)"
            >
              <mat-icon svgIcon="chevron-left" />
            </button>
            <button
              type="button"
              class="nf-btn nf-btn--icon"
              aria-label="Próxima página"
              [disabled]="page >= totalPages()"
              (click)="goTo(page + 1)"
            >
              <mat-icon svgIcon="chevron-right" />
            </button>
            <label class="page-size">
              Por página
              <select [value]="pageSize" (change)="setPageSize($event)">
                @for (size of pageSizes; track size) {
                  <option [value]="size">{{ size }}</option>
                }
              </select>
            </label>
          </nav>
        }
      </footer>
    </section>
  `,
  styleUrl: './invoices.page.scss',
  changeDetection: ChangeDetectionStrategy.Default,
})
export class InvoicesPage {
  private readonly invoiceService = inject(InvoiceService);
  private readonly dataRefresh = inject(DataRefreshService);
  private readonly apiError = inject(ApiErrorService);
  private readonly exporter = inject(ExportService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly datePipe = new DatePipe('pt-BR');

  readonly statusOptions: Array<{ value: StatusFilter; label: string }> = [
    { value: 'all', label: 'Todas' },
    { value: 'Open', label: 'Abertas' },
    { value: 'Closed', label: 'Fechadas' },
  ];
  readonly pageSizes = [25, 50, 100];

  invoices: InvoiceSummary[] = [];
  error: UiError | null = null;
  loading = true;

  query = '';
  statusFilter: StatusFilter = 'all';
  from = '';
  to = '';
  page = 1;
  pageSize = 50;

  readonly columns: ColDef[] = [
    {
      field: 'number',
      headerName: 'Número',
      width: 110,
      valueFormatter: (params: ValueFormatterParams) =>
        params.node?.rowPinned ? String(params.value ?? '') : `#${params.value}`,
    },
    {
      field: 'createdAt',
      headerName: 'Emissão',
      width: 165,
      valueFormatter: (params: ValueFormatterParams) =>
        params.value ? (this.datePipe.transform(params.value, 'dd/MM/yyyy HH:mm') ?? '') : '',
    },
    { field: 'itemCount', headerName: 'Itens', width: 100, type: 'numericColumn' },
    {
      colId: 'situacao',
      headerName: 'Situação',
      width: 140,
      sortable: false,
      valueGetter: (params) => {
        const row = params.data as InvoiceSummary | undefined;
        return row ? toDisplayState(row.status, row.closure) : '';
      },
      cellRenderer: (params: { value: string; node: { rowPinned?: string | null } }) => {
        if (params.node?.rowPinned || !params.value) return '';
        const span = document.createElement('span');
        span.className = `status-badge ${CLASSES[params.value] ?? ''}`;
        span.textContent = LABELS[params.value] ?? params.value;
        return span;
      },
    },
    {
      field: 'createdBy',
      headerName: 'Usuário',
      width: 170,
    },
    {
      colId: 'fechamento',
      headerName: 'Fechamento',
      flex: 1,
      minWidth: 170,
      sortable: false,
      valueGetter: (params) => {
        const row = params.data as InvoiceSummary | undefined;
        if (!row) return '';
        if (row.closedAt) return this.datePipe.transform(row.closedAt, 'dd/MM/yyyy HH:mm') ?? '';
        if (row.closure?.state === 'Pending') return 'reconciliando…';
        if (row.closure?.state === 'Rejected') return row.closure.errorMessage ?? 'rejeitado';
        return '—';
      },
    },
  ];

  /**
   * Campos materializados em vez de getters: o AG Grid recebe referências
   * estáveis, e o template não recalcula filtro a cada ciclo de detecção.
   */
  filtered: InvoiceSummary[] = [];
  pageRows: InvoiceSummary[] = [];
  totalRow: Array<Record<string, unknown>> = [];

  private computeFiltered(): InvoiceSummary[] {
    const term = this.query.trim().toLocaleLowerCase('pt-BR');
    const fromTime = this.from ? new Date(`${this.from}T00:00:00`).getTime() : null;
    const toTime = this.to ? new Date(`${this.to}T23:59:59`).getTime() : null;

    return this.invoices.filter((invoice) => {
      if (this.statusFilter !== 'all' && invoice.status !== (this.statusFilter as InvoiceStatus)) {
        return false;
      }
      const created = new Date(invoice.createdAt).getTime();
      if (fromTime !== null && created < fromTime) return false;
      if (toTime !== null && created > toTime) return false;
      // O resumo não traz os itens, então a busca é pelo número — com ou sem
      // "#". Prometer busca por produto aqui seria mentir para o operador.
      if (term && !`#${invoice.number}`.includes(term.replace(/^#?/, '#'))) return false;
      return true;
    });
  }

  /** Refaz filtro, página e totais. Chamado após carregar ou mudar filtro. */
  private refresh(): void {
    this.filtered = this.computeFiltered();
    const lastPage = this.totalPages();
    if (this.page > lastPage) this.page = lastPage;

    const start = (this.page - 1) * this.pageSize;
    this.pageRows = this.filtered.slice(start, start + this.pageSize);
    this.totalRow = [
      {
        number: `Total — ${this.filtered.length} ${this.filtered.length === 1 ? 'nota' : 'notas'}`,
        itemCount: this.filtered.reduce((sum, invoice) => sum + invoice.itemCount, 0),
      },
    ];
  }

  constructor() {
    // Recarrega quando outra parte do sistema mexe nesta área —
    // o assistente criando produto ou nota, por exemplo.
    this.dataRefresh
      .on('notas')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
    this.load();
    // /notas/:id abre o detalhe em modal sobre a listagem, preservando o link direto.
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = params.get('id');
      if (id) this.openDetail(id, false);
    });

    // O favorito é uma consulta salva: /notas?situacao=abertas abre filtrado.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.statusFilter = params.get('situacao') === 'abertas' ? 'Open' : 'all';
      this.page = 1;
      this.refresh();
    });
  }

  totalPages(): number {
    return Math.max(1, Math.ceil(this.filtered.length / this.pageSize));
  }

  rangeLabel(): string {
    const total = this.filtered.length;
    if (total === 0) return '0 de 0';
    const start = (this.page - 1) * this.pageSize + 1;
    return `${start}–${Math.min(start + this.pageSize - 1, total)} de ${total}`;
  }

  goTo(page: number): void {
    this.page = Math.min(Math.max(1, page), this.totalPages());
    this.refresh();
  }

  setPageSize(event: Event): void {
    this.pageSize = Number((event.target as HTMLSelectElement).value);
    this.page = 1;
    this.refresh();
  }

  applyQuery(value: string): void {
    this.query = value;
    this.page = 1;
    this.refresh();
  }

  setStatus(value: StatusFilter): void {
    this.statusFilter = value;
    this.page = 1;
    this.refresh();
  }

  setFrom(event: Event): void {
    this.from = (event.target as HTMLInputElement).value;
    this.page = 1;
    this.refresh();
  }

  setTo(event: Event): void {
    this.to = (event.target as HTMLInputElement).value;
    this.page = 1;
    this.refresh();
  }

  load(): void {
    this.loading = true;
    this.error = null;
    this.invoiceService
      .list('', 1, 200)
      .pipe(
        catchError((error: unknown) => {
          this.error = this.apiError.from(error);
          return of({ items: [], total: 0, page: 1, pageSize: 200 });
        }),
        finalize(() => (this.loading = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.invoices = result.items;
        this.refresh();
      });
  }

  exportCsv(): void {
    this.exporter.toCsv(
      this.filtered,
      [
        { header: 'Número', value: (invoice) => invoice.number },
        {
          header: 'Emissão',
          value: (invoice) => this.datePipe.transform(invoice.createdAt, 'dd/MM/yyyy HH:mm') ?? '',
        },
        { header: 'Itens', value: (invoice) => invoice.itemCount },
        {
          header: 'Situação',
          value: (invoice) => LABELS[toDisplayState(invoice.status, invoice.closure)] ?? '',
        },
        { header: 'Usuário', value: (invoice) => invoice.createdBy },
        { header: 'Fechada por', value: (invoice) => invoice.closedBy ?? '' },
        {
          header: 'Fechamento',
          value: (invoice) =>
            invoice.closedAt
              ? (this.datePipe.transform(invoice.closedAt, 'dd/MM/yyyy HH:mm') ?? '')
              : '',
        },
      ],
      'notas-fiscais',
    );
  }

  openCreate(): void {
    this.dialog
      .open(CreateInvoiceDialog, {
        width: 'min(760px, 94vw)',
        panelClass: 'nf-dialog',
        autoFocus: 'first-tabbable',
      })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((createdId?: string) => {
        this.load();
        if (createdId) this.openDetail(createdId);
      });
  }

  /**
   * Abre o detalhe em modal e reflete a nota na URL.
   *
   * A deduplicação usa o id do diálogo no MatDialog, que é singleton, e não um
   * campo da página: `/notas` e `/notas/:id` são definições de rota diferentes
   * para o mesmo componente, então navegar RECRIA a página — uma guarda por
   * instância nasceria zerada e deixaria dois modais da mesma nota empilhados.
   */
  openImport(): void {
    this.dialog
      .open(ImportInvoicesDialog, {
        width: 'min(640px, 94vw)',
        panelClass: 'nf-dialog',
        autoFocus: 'first-tabbable',
      })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((imported?: boolean) => {
        if (imported) this.load();
      });
  }

  openDetail(id: string, pushUrl = true): void {
    if (this.dialog.getDialogById(id)) return;

    if (pushUrl) void this.router.navigate(['/notas', id]);

    this.dialog
      .open(InvoiceDetailDialog, {
        id,
        width: 'min(900px, 94vw)',
        panelClass: 'nf-dialog',
        data: { id },
        autoFocus: 'first-tabbable',
      })
      .afterClosed()
      .subscribe(() => {
        void this.router.navigate(['/notas']);
        this.load();
      });
  }
}
