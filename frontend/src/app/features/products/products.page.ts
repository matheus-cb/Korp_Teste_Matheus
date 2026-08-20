import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import type { ColDef } from 'ag-grid-community';
import {
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  finalize,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import type { UiError } from '../../core/models/api.models';
import type { Product } from '../../core/models/product.model';
import { ApiErrorService } from '../../core/services/api-error.service';
import { ExportService } from '../../core/services/export.service';
import { ProductService } from '../../core/services/product.service';
import { DataGridComponent } from '../../shared/data-grid.component';
import { RefreshButtonComponent } from '../../shared/refresh-button.component';
import { InlineAlertComponent } from '../../shared/inline-alert.component';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { ProductFormDialog } from './product-form.dialog';
import { DataRefreshService } from '../../core/services/data-refresh.service';

interface BalanceState {
  label: string;
  className: string;
}

function balanceState(product: Pick<Product, 'balance' | 'tracksStock'>): BalanceState {
  // Sem controle de estoque não existe saldo a interpretar.
  if (!product.tracksStock) return { label: 'Sem controle', className: 'status-open' };
  if (product.balance === 0) return { label: 'Crítico', className: 'status-rejected' };
  if (product.balance <= 5) return { label: 'Saldo baixo', className: 'status-pending' };
  return { label: 'Normal', className: 'status-done' };
}

@Component({
  selector: 'app-products-page',
  imports: [
    DataGridComponent,
    InlineAlertComponent,
    MatIconModule,
    PageHeaderComponent, RefreshButtonComponent],
  template: `
    <app-page-header module="Estoque" title="Produtos">
      <app-refresh-button [loading]="loading" (refresh)="reload()" />
      <button type="button" class="nf-btn" (click)="exportCsv()">
        <mat-icon svgIcon="download" />
        Exportar
      </button>
      <button type="button" class="nf-btn nf-btn--primary" (click)="openCreate()">
        <mat-icon svgIcon="plus" />
        Novo produto
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
          placeholder="Código ou descrição"
          aria-label="Buscar produto"
            (input)="search(searchInput.value)"
          />
        </label>
      </div>

      @if (listError) {
        <div class="state-padding">
          <app-inline-alert
            tone="error"
            [title]="listError.title"
            [message]="listError.message"
            [traceId]="listError.traceId"
            [retryable]="true"
            (retry)="reload()"
          />
        </div>
      } @else {
        <div class="grid-area">
          <app-data-grid
            [rowData]="products"
            [columnDefs]="columns"
            [pinnedBottomRowData]="totalRow"
            [emptyMessage]="emptyMessage()"
          />
        </div>
      }

      <!-- A contagem vive no rodape, nao na barra de filtros: ela descreve o
           resultado, e ali competia por espaco com a busca. -->
      <footer class="card-foot">
        <span>{{ products.length }} {{ products.length === 1 ? 'produto' : 'produtos' }}</span>
      </footer>
    </section>
  `,
  styleUrl: './products.page.scss',
  changeDetection: ChangeDetectionStrategy.Default,
})
export class ProductsPage {
  private readonly productService = inject(ProductService);
  private readonly dataRefresh = inject(DataRefreshService);
  private readonly apiError = inject(ApiErrorService);
  private readonly exporter = inject(ExportService);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searchTerms = new Subject<string>();

  products: Product[] = [];
  totalRow: Array<Record<string, unknown>> = [];
  currentQuery = '';
  loading = true;
  listError: UiError | null = null;

  readonly columns: ColDef[] = [
    {
      field: 'code',
      headerName: 'Código',
      width: 190,
      cellClass: 'cell-code',
      valueFormatter: (params) => (params.node?.rowPinned ? 'Total em estoque' : params.value),
    },
    { field: 'description', headerName: 'Descrição', flex: 1, minWidth: 200 },
    {
      field: 'balance',
      headerName: 'Saldo',
      width: 120,
      type: 'numericColumn',
      cellClass: 'numeric',
      valueFormatter: (params) => {
        if (params.node?.rowPinned) return String(params.value ?? '');
        const product = params.data as Product | undefined;
        return product && !product.tracksStock ? '—' : String(params.value ?? '');
      },
    },
    {
      colId: 'situacao',
      headerName: 'Situação',
      width: 140,
      sortable: false,
      // Devolve um nó do DOM em vez de string para não abrir caminho de HTML.
      cellRenderer: (params: { data?: Product; node: { rowPinned?: string | null } }) => {
        if (params.node?.rowPinned || !params.data) return '';
        const state = balanceState(params.data);
        const span = document.createElement('span');
        span.className = `status-badge ${state.className}`;
        span.textContent = state.label;
        return span;
      },
    },
  ];

  constructor() {
    // Recarrega quando outra parte do sistema mexe nesta área —
    // o assistente criando produto ou nota, por exemplo.
    this.dataRefresh
      .on('produtos')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reload());
    this.searchTerms
      .pipe(
        startWith(''),
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((query) => {
          this.currentQuery = query;
          this.loading = true;
          this.listError = null;
          return this.productService.list(query, 1, 100).pipe(
            catchError((error: unknown) => {
              this.listError = this.apiError.from(error);
              return of({ items: [], total: 0, page: 1, pageSize: 100 });
            }),
            finalize(() => (this.loading = false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.products = result.items;
        this.totalRow = [
          {
            code: 'Total em estoque',
            description: '',
            // Só soma quem controla estoque: item sem controle não tem saldo.
        balance: result.items
          .filter((product) => product.tracksStock)
          .reduce((sum, product) => sum + product.balance, 0),
          },
        ];
      });
  }

  emptyMessage(): string {
    return this.currentQuery
      ? 'Nenhum produto encontrado para esta busca'
      : 'Cadastre o primeiro produto para começar a emitir notas';
  }

  search(value: string): void {
    this.searchTerms.next(value.trim());
  }

  reload(): void {
    this.searchTerms.next(this.currentQuery);
  }

  exportCsv(): void {
    this.exporter.toCsv(
      this.products,
      [
        { header: 'Código', value: (product) => product.code },
        { header: 'Descrição', value: (product) => product.description },
        { header: 'Saldo', value: (product) => (product.tracksStock ? product.balance : '') },
        { header: 'Controla estoque', value: (product) => (product.tracksStock ? 'Sim' : 'Não') },
        { header: 'Situação', value: (product) => balanceState(product).label },
      ],
      'produtos',
    );
  }

  openCreate(): void {
    this.dialog
      .open(ProductFormDialog, {
        width: 'min(540px, 94vw)',
        panelClass: 'nf-dialog',
        autoFocus: 'first-tabbable',
      })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((created?: Product) => {
        if (created) this.reload();
      });
  }
}
