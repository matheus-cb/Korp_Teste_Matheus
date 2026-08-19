import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import { AllCommunityModule, ModuleRegistry, themeBalham } from 'ag-grid-community';
import type { ColDef, GridReadyEvent, RowClickedEvent } from 'ag-grid-community';

// A partir da v33 o AG Grid exige registro explícito de módulos, senão o grid
// lança em runtime. Registrado uma única vez, no carregamento deste arquivo.
ModuleRegistry.registerModules([AllCommunityModule]);

/**
 * Tema Balham nativo com três parâmetros ajustados para casar com o restante
 * da aplicação: cabeçalho escuro e densidade confortável. Não reescrevemos o
 * tema — apenas usamos a API dele, então voltar ao padrão é apagar o
 * `withParams`.
 */
const THEME = themeBalham.withParams({
  headerBackgroundColor: '#243342',
  headerTextColor: '#eef2f5',
  accentColor: '#0f5a50',
});

/** Sem isto o grid mostra "No Rows To Show" e afins em inglês. */
const LOCALE_PT_BR: Record<string, string> = {
  noRowsToShow: 'Nenhum registro encontrado',
  loadingOoo: 'Carregando…',
  page: 'Página',
  to: 'até',
  of: 'de',
  nextPage: 'Próxima página',
  lastPage: 'Última página',
  firstPage: 'Primeira página',
  previousPage: 'Página anterior',
  pageSizeSelectorLabel: 'Por página:',
  ariaSortableColumn: 'Coluna ordenável',
  blank: 'Vazio',
};

@Component({
  selector: 'app-data-grid',
  imports: [AgGridAngular],
  template: `
    <ag-grid-angular
      class="grid"
      [theme]="theme"
      [rowData]="rowData()"
      [columnDefs]="columnDefs()"
      [pinnedBottomRowData]="pinnedBottomRowData()"
      [defaultColDef]="defaultColDef"
      [localeText]="locale"
      [rowHeight]="44"
      [headerHeight]="34"
      [suppressCellFocus]="true"
      [domLayout]="domLayout()"
      [overlayNoRowsTemplate]="emptyMessage()"
      (rowClicked)="onRowClicked($event)"
      (gridReady)="gridReady.emit($event)"
    />
  `,
  styles: `
    :host {
      display: block;
      min-height: 0;
      flex: 1;
    }

    .grid {
      display: block;
      width: 100%;
      height: 100%;
    }

    /* Linha fixada do rodapé: é o total, então precisa ler como total. */
    :host ::ng-deep .ag-floating-bottom {
      border-top: 1px solid var(--n-300);
      font-weight: 660;
    }

    :host ::ng-deep .ag-row-hover {
      cursor: pointer;
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class DataGridComponent<T> {
  readonly rowData = input<T[] | null>([]);
  readonly columnDefs = input.required<ColDef[]>();
  readonly pinnedBottomRowData = input<unknown[]>([]);
  readonly domLayout = input<'normal' | 'autoHeight'>('normal');
  readonly emptyMessage = input<string>('Nenhum registro encontrado');

  readonly rowClicked = output<T>();
  readonly gridReady = output<GridReadyEvent>();

  protected readonly theme = THEME;
  protected readonly locale = LOCALE_PT_BR;

  protected readonly defaultColDef: ColDef = {
    sortable: true,
    resizable: true,
    suppressMovable: true,
  };

  protected onRowClicked(event: RowClickedEvent): void {
    // A linha de total é fixada no rodapé e não representa um registro.
    if (event.rowPinned) return;
    this.rowClicked.emit(event.data as T);
  }
}
