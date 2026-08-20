import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import type { ColDef, ValueFormatterParams } from 'ag-grid-community';
import { catchError, finalize, of } from 'rxjs';
import type { UiError } from '../../core/models/api.models';
import type { StockMovement } from '../../core/models/movement.model';
import { ApiErrorService } from '../../core/services/api-error.service';
import { ExportService } from '../../core/services/export.service';
import { MovementService } from '../../core/services/movement.service';
import { DataGridComponent } from '../../shared/data-grid.component';
import { RefreshButtonComponent } from '../../shared/refresh-button.component';
import { InlineAlertComponent } from '../../shared/inline-alert.component';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { DataRefreshService } from '../../core/services/data-refresh.service';

/**
 * Extrato de movimentação (UC-09). Torna a auditoria visível: cada baixa com
 * saldo antes e depois, e a nota que a originou.
 */
@Component({
  selector: 'app-movements-page',
  imports: [DataGridComponent, InlineAlertComponent, MatIconModule, PageHeaderComponent, RefreshButtonComponent],
  template: `
    <app-page-header module="Fiscal" title="Movimentações">
      <app-refresh-button [loading]="loading" (refresh)="load()" />
      <button type="button" class="nf-btn" (click)="exportCsv()">
        <mat-icon svgIcon="download" />
        Exportar
      </button>
    </app-page-header>

    <div class="toolbar">
      <span class="hint">Cada linha é uma baixa concluída, com o saldo antes e depois.</span>
      <span class="count">
        {{ movements.length }} {{ movements.length === 1 ? 'movimento' : 'movimentos' }}
      </span>
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
      <app-data-grid
        [rowData]="movements"
        [columnDefs]="columns"
        [pinnedBottomRowData]="totalRow"
        emptyMessage="Nenhuma movimentação registrada"
        (rowClicked)="openInvoice($event)"
      />
    }
  `,
  styles: `
    :host {
      display: flex;
      min-height: 0;
      flex: 1;
      flex-direction: column;
    }

    .toolbar {
      display: flex;
      align-items: center;
      padding: var(--sp-2) var(--sp-4);
      border-bottom: 1px solid var(--n-200);
      background: var(--n-25);
      gap: var(--sp-2);
    }

    .hint,
    .count {
      color: var(--n-500);
      font-size: var(--fs-sm);
    }

    .count {
      margin-left: auto;
    }

    .state-padding {
      padding: var(--sp-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class MovementsPage {
  private readonly movementService = inject(MovementService);
  private readonly dataRefresh = inject(DataRefreshService);
  private readonly apiError = inject(ApiErrorService);
  private readonly exporter = inject(ExportService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly datePipe = new DatePipe('pt-BR');

  movements: StockMovement[] = [];
  totalRow: Array<Record<string, unknown>> = [];
  error: UiError | null = null;
  /** Esta tela não tinha estado de carregamento: atualizar não dava sinal nenhum. */
  loading = true;

  readonly columns: ColDef[] = [
    {
      field: 'createdAt',
      headerName: 'Data',
      width: 175,
      valueFormatter: (params: ValueFormatterParams) =>
        params.node?.rowPinned
          ? String(params.value ?? '')
          : (this.datePipe.transform(params.value, 'dd/MM/yyyy HH:mm:ss') ?? ''),
    },
    { field: 'code', headerName: 'Código', width: 170, cellClass: 'cell-code' },
    { field: 'description', headerName: 'Produto', flex: 1, minWidth: 180 },
    { field: 'quantity', headerName: 'Baixa', width: 100, type: 'numericColumn' },
    { field: 'balanceBefore', headerName: 'Saldo antes', width: 130, type: 'numericColumn' },
    { field: 'balanceAfter', headerName: 'Saldo depois', width: 135, type: 'numericColumn' },
  ];

  constructor() {
    // Recarrega quando outra parte do sistema mexe nesta área —
    // o assistente criando produto ou nota, por exemplo.
    this.dataRefresh
      .on('movimentacoes')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
    this.load();
  }

  load(): void {
    this.error = null;
    this.loading = true;
    this.movementService
      .list(1, 200)
      .pipe(
        catchError((error: unknown) => {
          this.error = this.apiError.from(error);
          return of({ items: [], total: 0, page: 1, pageSize: 200 });
        }),
        finalize(() => (this.loading = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.movements = result.items;
        const total = result.items.length;
        this.totalRow = [
          {
            createdAt: `Total — ${total} ${total === 1 ? 'movimento' : 'movimentos'}`,
            quantity: result.items.reduce((sum, movement) => sum + movement.quantity, 0),
          },
        ];
      });
  }

  /** Da movimentação para a nota que a originou — rastreabilidade nos dois sentidos. */
  openInvoice(movement: StockMovement): void {
    void this.router.navigate(['/notas', movement.invoiceId]);
  }

  exportCsv(): void {
    this.exporter.toCsv(
      this.movements,
      [
        {
          header: 'Data',
          value: (m) => this.datePipe.transform(m.createdAt, 'dd/MM/yyyy HH:mm:ss') ?? '',
        },
        { header: 'Código', value: (m) => m.code },
        { header: 'Produto', value: (m) => m.description },
        { header: 'Baixa', value: (m) => m.quantity },
        { header: 'Saldo antes', value: (m) => m.balanceBefore },
        { header: 'Saldo depois', value: (m) => m.balanceAfter },
      ],
      'movimentacoes',
    );
  }
}
