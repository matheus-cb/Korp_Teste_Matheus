import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import { AllCommunityModule, ModuleRegistry, themeBalham } from 'ag-grid-community';
import type { ColDef, GridReadyEvent, RowClickedEvent } from 'ag-grid-community';

// A partir da v33 o AG Grid exige registro explícito de módulos, senão o grid
// lança em runtime. Registrado uma única vez, no carregamento deste arquivo.
ModuleRegistry.registerModules([AllCommunityModule]);

/**
 * Tema Balham nativo, ajustado pela API do proprio tema — nao reescrevemos o
 * tema, e voltar ao padrao e apagar o `withParams`.
 *
 * O cabeçalho era `#243342`, quase preto. Num card claro ele pesava demais:
 * virava o elemento mais forte da tela e competia com os badges de situação,
 * que são justamente o que precisa saltar. Agora é cinza claro com texto
 * escuro, e a separação vem da borda inferior — o mesmo peso visual do resto
 * da interface.
 *
 * Os valores são literais porque a API do tema não resolve `var()`; são os
 * mesmos de `--n-100`, `--n-700`, `--n-200` e `--brand-600` em `styles.scss`.
 */
const THEME = themeBalham.withParams({
  headerBackgroundColor: '#eceff2',
  headerTextColor: '#343d46',
  headerFontWeight: 660,
  borderColor: '#dde3e8',
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

    /* Sem o fundo escuro, e a borda que separa cabecalho de dados. */
    :host ::ng-deep .ag-header {
      border-bottom: 1px solid var(--n-300);
    }

    /*
     * Separadores de coluna no cabecalho: com fundo claro, sem eles as colunas
     * se misturam.
     *
     * NAO declare "position: relative" no .ag-header-cell. O grid posiciona
     * cada celula com "position: absolute; left: X"; tornando-a relativa, o
     * "left" passa a somar sobre a posicao natural e os cabecalhos acumulam
     * deslocamento -- as celulas ficam no lugar e o cabecalho sai da tela.
     * A celula ja e contexto de posicionamento, e o ::after ancora nela.
     */
    :host ::ng-deep .ag-header-cell:not(:last-child)::after {
      position: absolute;
      top: 25%;
      right: 0;
      width: 1px;
      height: 50%;
      background: var(--n-300);
      content: '';
    }

    /* Linha fixada do rodapé: é o total, então precisa ler como total. */
    :host ::ng-deep .ag-floating-bottom {
      border-top: 1px solid var(--n-300);
      background: var(--n-25);
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
