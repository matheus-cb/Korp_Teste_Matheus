import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import type { OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { finalize } from 'rxjs';
import type { AiDraftItem } from '../../core/models/ai-draft.model';
import type { UiError } from '../../core/models/api.models';
import type { Invoice, InvoiceItem } from '../../core/models/invoice.model';
import type { Product } from '../../core/models/product.model';
import { ApiErrorService } from '../../core/services/api-error.service';
import { DraftTransferService } from '../../core/services/draft-transfer.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { NotificationService } from '../../core/services/notification.service';
import { ProductService } from '../../core/services/product.service';
import { InlineAlertComponent } from '../../shared/inline-alert.component';
import { ModalShellComponent } from '../../shared/modal-shell.component';

/** Emissão de nota: ação dentro da listagem, portanto em modal. */
@Component({
  selector: 'app-create-invoice-dialog',
  imports: [
    InlineAlertComponent,
    MatIconModule,
    ModalShellComponent,
    ReactiveFormsModule,
  ],
  template: `
    <app-modal-shell
      [title]="editing ? 'Editar nota fiscal' : 'Nova nota fiscal'"
      [confirmLabel]="editing ? 'Salvar alterações' : 'Criar nota'"
      dismissLabel="Sair"
      busyLabel="Criando…"
      [busy]="saving"
      [canConfirm]="items.length > 0"
      (confirm)="createInvoice()"
      (dismiss)="close()"
    >
      <form class="picker" [formGroup]="itemForm" novalidate (ngSubmit)="addItem()">
        <div class="nf-field product">
          <label class="nf-label" for="item-product">Produto</label>
          <select id="item-product" class="nf-select" formControlName="productId">
            <option value="">Selecione um produto…</option>
            @for (product of products; track product.id) {
              <option [value]="product.id">
                {{ product.code }} — {{ product.description }}{{ product.tracksStock ? ' (' + product.balance + ')' : ' (sem controle)' }}
              </option>
            }
          </select>
        </div>

        <div class="nf-field quantity">
          <label class="nf-label" for="item-quantity">Quantidade</label>
          <input
            id="item-quantity"
            class="nf-input"
            type="number"
            formControlName="quantity"
            min="1"
            inputmode="numeric"
          />
        </div>

        <button type="submit" class="nf-btn nf-btn--primary add" [disabled]="items.length >= 20">
          <mat-icon svgIcon="plus" />
          Adicionar
        </button>
      </form>

      @if (productsError) {
        <app-inline-alert
          tone="error"
          [title]="productsError.title"
          [message]="productsError.message"
          [traceId]="productsError.traceId"
          [retryable]="true"
          (retry)="loadProducts()"
        />
      }

      @if (items.length === 0) {
        <p class="empty">Adicione ao menos um produto para emitir a nota.</p>
      } @else {
        <table class="data-table items">
          <thead>
            <tr>
              <th scope="col">Código</th>
              <th scope="col">Produto</th>
              <th scope="col" class="numeric">Qtd.</th>
              <th scope="col"></th>
            </tr>
          </thead>
          <tbody>
            @for (item of items; track item.productId) {
              <tr>
                <td class="code">{{ item.code }}</td>
                <td>{{ item.description }}</td>
                <td class="numeric">{{ item.quantity }}</td>
                <td class="row-action">
                  <button
                    type="button"
                    class="nf-btn nf-btn--icon"
                    [attr.aria-label]="'Remover ' + item.code"
                    (click)="removeItem(item.productId)"
                  >
                    <mat-icon svgIcon="x" />
                  </button>
                </td>
              </tr>
            }
          </tbody>
          <tfoot>
            <tr>
              <td colspan="2">Total de unidades</td>
              <td class="numeric">{{ totalUnits }}</td>
              <td></td>
            </tr>
          </tfoot>
        </table>
      }

      @if (createError) {
        <app-inline-alert
          tone="error"
          [title]="createError.title"
          [message]="createError.message"
          [traceId]="createError.traceId"
        />
      }
    </app-modal-shell>
  `,
  styles: `
    .picker {
      display: flex;
      align-items: flex-end;
      margin-bottom: var(--sp-4);
      gap: var(--sp-2);
    }

    .picker .product {
      flex: 1;
    }

    .picker .quantity {
      width: 120px;
      flex: none;
    }

    /* Alinha com a altura dos campos (32px), não com a do botão de barra. */
    .add {
      height: 32px;
      flex: none;
    }

    .empty {
      margin: 0;
      padding: var(--sp-5);
      border: 1px dashed var(--n-300);
      border-radius: var(--r-md);
      color: var(--n-500);
      font-size: var(--fs-sm);
      text-align: center;
    }

    .items {
      border: 1px solid var(--n-200);
    }

    .row-action {
      width: 48px;
      text-align: right;
    }

    .code {
      color: var(--n-600);
      font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
      font-size: var(--fs-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class CreateInvoiceDialog implements OnInit {
  private readonly dialogRef = inject<MatDialogRef<CreateInvoiceDialog, string>>(MatDialogRef);
  private readonly formBuilder = inject(FormBuilder);
  private readonly productService = inject(ProductService);
  private readonly invoiceService = inject(InvoiceService);
  private readonly apiError = inject(ApiErrorService);
  private readonly notification = inject(NotificationService);
  private readonly transfer = inject(DraftTransferService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly data = inject<{ invoice?: Invoice } | null>(MAT_DIALOG_DATA, { optional: true });
  readonly editing = !!this.data?.invoice;

  readonly itemForm = this.formBuilder.nonNullable.group({
    productId: ['', Validators.required],
    quantity: [
      1,
      [
        Validators.required,
        Validators.min(1),
        Validators.max(999_999),
        Validators.pattern(/^\d+$/),
      ],
    ],
  });

  products: Product[] = [];
  items: InvoiceItem[] = [];
  saving = false;
  productsError: UiError | null = null;
  createError: UiError | null = null;

  ngOnInit(): void {
    // Rascunho vindo do assistente entra já preenchido para revisão.
    if (this.data?.invoice) this.items = this.data.invoice.items.map((item) => ({ ...item }));
    else {
      const draft = this.transfer.take();
      if (draft.length) this.mergeDraft(draft);
    }
    this.loadProducts();
  }

  get totalUnits(): number {
    return this.items.reduce((total, item) => total + item.quantity, 0);
  }

  close(): void {
    if (!this.saving) this.dialogRef.close();
  }

  loadProducts(): void {
    this.productsError = null;
    this.productService
      .list('', 1, 100)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => (this.products = result.items),
        error: (error: unknown) => (this.productsError = this.apiError.from(error)),
      });
  }

  addItem(): void {
    this.itemForm.markAllAsTouched();
    if (this.itemForm.invalid || this.items.length >= 20) return;

    const { productId, quantity } = this.itemForm.getRawValue();
    const product = this.products.find((candidate) => candidate.id === productId);
    if (!product) return;

    this.mergeItem({
      productId: product.id,
      code: product.code,
      description: product.description,
      quantity,
    });
    this.itemForm.reset({ productId: '', quantity: 1 });
  }

  removeItem(productId: string): void {
    this.items = this.items.filter((item) => item.productId !== productId);
  }

  createInvoice(): void {
    if (!this.items.length || this.saving) return;
    this.saving = true;
    this.createError = null;
    const request = { items: this.items.map((item) => ({ productId: item.productId, quantity: item.quantity })) };
    const existing = this.data?.invoice;
    const request$ = existing
      ? this.invoiceService.update(existing.id, request, existing.version ?? '')
      : this.invoiceService.create(request);
    request$
      .pipe(
        finalize(() => (this.saving = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (invoice) => {
          this.notification.success(this.editing ? 'Nota atualizada' : 'Nota criada', `Nota #${invoice.number} está aberta e pronta para fechamento.`);
          this.dialogRef.close(invoice.id);
        },
        error: (error: unknown) => (this.createError = this.apiError.from(error)),
      });
  }

  private mergeDraft(draftItems: AiDraftItem[]): void {
    for (const draft of draftItems.slice(0, 20)) {
      if (draft.quantity <= 0 || !Number.isInteger(draft.quantity)) continue;
      const product = this.products.find((candidate) => candidate.id === draft.productId);
      this.mergeItem({
        productId: draft.productId,
        // A lista do catálogo chega em paralelo. Enquanto ela não responde,
        // preserve o retrato devolvido pelo assistente em vez de mostrar
        // "—" e "Produto do rascunho" para um item já reconhecido.
        code: product?.code ?? draft.code,
        description: product?.description ?? draft.description,
        quantity: draft.quantity,
      });
    }
  }

  /** Mesmo produto adicionado duas vezes soma, em vez de duplicar linha. */
  private mergeItem(item: InvoiceItem): void {
    const existing = this.items.find((candidate) => candidate.productId === item.productId);
    if (existing) {
      existing.quantity = Math.min(999_999, existing.quantity + item.quantity);
      this.items = [...this.items];
      return;
    }
    this.items = [...this.items, item];
  }
}
