import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { catchError, finalize, forkJoin, of } from 'rxjs';
import type { UiError } from '../../core/models/api.models';
import type { InvoiceSummary } from '../../core/models/invoice.model';
import type { Product } from '../../core/models/product.model';
import { ApiErrorService } from '../../core/services/api-error.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { ProductService } from '../../core/services/product.service';
import { InlineAlertComponent } from '../../shared/inline-alert.component';
import { LoadingStateComponent } from '../../shared/loading-state.component';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusPillComponent, toDisplayState } from '../../shared/status-pill.component';

interface DashboardData {
  products: Product[];
  invoices: InvoiceSummary[];
}

@Component({
  selector: 'app-dashboard-page',
  imports: [
    DatePipe,
    InlineAlertComponent,
    LoadingStateComponent,
    MatIconModule,
    PageHeaderComponent,
    RouterLink,
    StatusPillComponent,
  ],
  template: `
    <app-page-header module="Operação" title="Visão geral">
      <button type="button" class="nf-btn nf-btn--icon" aria-label="Atualizar" (click)="load()">
        <mat-icon svgIcon="refresh-cw" />
      </button>
      <a class="nf-btn nf-btn--primary" routerLink="/notas">
        <mat-icon svgIcon="plus" />
        Nova nota
      </a>
    </app-page-header>

    <div class="page">
      @if (loading) {
        <app-loading-state message="Consolidando a operação…" />
      } @else {
        @if (error) {
          <app-inline-alert
            tone="warning"
            [title]="error.title"
            [message]="error.message"
            [traceId]="error.traceId"
            [retryable]="true"
            (retry)="load()"
          />
        }

        <!-- Pendências: o que exige ação vem primeiro e é clicável. -->
        <section class="tiles" aria-label="Pendências da operação">
          <a class="tile" routerLink="/notas" [class.alert]="pendingCount > 0">
            <span class="tile-label">Fechamentos pendentes</span>
            <strong class="tile-value">{{ pendingCount }}</strong>
            <span class="tile-hint">Aguardando reconciliação com o Estoque</span>
          </a>
          <a class="tile" routerLink="/notas" [class.danger]="rejectedCount > 0">
            <span class="tile-label">Fechamentos rejeitados</span>
            <strong class="tile-value">{{ rejectedCount }}</strong>
            <span class="tile-hint">Revise os itens antes de tentar de novo</span>
          </a>
          <a class="tile" routerLink="/notas">
            <span class="tile-label">Notas abertas</span>
            <strong class="tile-value">{{ openInvoices.length }}</strong>
            <span class="tile-hint">Ainda podem ser fechadas</span>
          </a>
          <a class="tile" routerLink="/produtos" [class.danger]="criticalProducts.length > 0">
            <span class="tile-label">Saldo crítico</span>
            <strong class="tile-value">{{ criticalProducts.length }}</strong>
            <span class="tile-hint">Produtos zerados no catálogo</span>
          </a>
        </section>

        <section class="panels">
          <article class="panel">
            <header class="panel-head">
              <h2>Notas recentes</h2>
              <a class="panel-link" routerLink="/notas">Ver todas</a>
            </header>
            @if (recentInvoices.length === 0) {
              <p class="panel-empty">Nenhuma nota emitida ainda.</p>
            } @else {
              <ul class="rows">
                @for (invoice of recentInvoices; track invoice.id) {
                  <li>
                    <a class="row" [routerLink]="['/notas', invoice.id]">
                      <span class="row-number">#{{ invoice.number }}</span>
                      <span class="row-main">
                        {{ invoice.itemCount }} {{ invoice.itemCount === 1 ? 'item' : 'itens' }}
                        <small>{{ invoice.createdAt | date: 'dd/MM/yyyy HH:mm' }}</small>
                      </span>
                      <app-status-pill [state]="toDisplayState(invoice.status, invoice.closure)" />
                    </a>
                  </li>
                }
              </ul>
            }
          </article>

          <article class="panel">
            <header class="panel-head">
              <h2>Produtos que pedem atenção</h2>
              <a class="panel-link" routerLink="/produtos">Ver catálogo</a>
            </header>
            @if (attentionProducts.length === 0) {
              <p class="panel-empty">Nenhum produto com saldo baixo ou zerado.</p>
            } @else {
              <ul class="rows">
                @for (product of attentionProducts; track product.id) {
                  <li>
                    <a class="row" routerLink="/produtos">
                      <span class="row-code">{{ product.code }}</span>
                      <span class="row-main">{{ product.description }}</span>
                      <span
                        class="status-badge"
                        [class.status-rejected]="product.balance === 0"
                        [class.status-pending]="product.balance > 0"
                      >
                        {{ product.balance }} {{ product.balance === 1 ? 'unidade' : 'unidades' }}
                      </span>
                    </a>
                  </li>
                }
              </ul>
            }
          </article>
        </section>
      }
    </div>
  `,
  styleUrl: './dashboard.page.scss',
  changeDetection: ChangeDetectionStrategy.Default,
})
export class DashboardPage {
  protected readonly toDisplayState = toDisplayState;
  private readonly productService = inject(ProductService);
  private readonly invoiceService = inject(InvoiceService);
  private readonly apiError = inject(ApiErrorService);
  private readonly destroyRef = inject(DestroyRef);

  loading = true;
  error: UiError | null = null;
  data: DashboardData = { products: [], invoices: [] };

  constructor() {
    this.load();
  }

  get pendingCount(): number {
    return this.data.invoices.filter((invoice) => invoice.closure?.state === 'Pending').length;
  }

  get rejectedCount(): number {
    return this.data.invoices.filter((invoice) => invoice.closure?.state === 'Rejected').length;
  }

  get openInvoices(): InvoiceSummary[] {
    return this.data.invoices.filter((invoice) => invoice.status === 'Open');
  }

  get criticalProducts(): Product[] {
    return this.data.products.filter((product) => product.balance === 0);
  }

  /** Zerados primeiro: são os que travam uma emissão. */
  get attentionProducts(): Product[] {
    return this.data.products
      .filter((product) => product.balance <= 5)
      .sort((first, second) => first.balance - second.balance)
      .slice(0, 5);
  }

  get recentInvoices(): InvoiceSummary[] {
    return [...this.data.invoices]
      .sort(
        (first, second) =>
          new Date(second.createdAt).getTime() - new Date(first.createdAt).getTime(),
      )
      .slice(0, 5);
  }

  load(): void {
    this.loading = true;
    this.error = null;
    forkJoin({
      products: this.productService.list('', 1, 100).pipe(
        catchError((error: unknown) => {
          this.error = this.apiError.from(error);
          return of({ items: [], total: 0, page: 1, pageSize: 100 });
        }),
      ),
      invoices: this.invoiceService.list('', 1, 100).pipe(
        catchError((error: unknown) => {
          this.error = this.apiError.from(error);
          return of({ items: [], total: 0, page: 1, pageSize: 100 });
        }),
      ),
    })
      .pipe(
        finalize(() => (this.loading = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.data = {
          products: result.products.items,
          invoices: result.invoices.items,
        };
      });
  }
}
