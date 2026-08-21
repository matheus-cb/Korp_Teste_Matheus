import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { finalize } from 'rxjs';
import type { UiError } from '../../core/models/api.models';
import type { Product } from '../../core/models/product.model';
import { ApiErrorService } from '../../core/services/api-error.service';
import { NotificationService } from '../../core/services/notification.service';
import { ProductService } from '../../core/services/product.service';
import { InlineAlertComponent } from '../../shared/inline-alert.component';
import { ModalShellComponent } from '../../shared/modal-shell.component';

/**
 * Cadastro de produto: é uma ação dentro do catálogo, então acontece em modal
 * e não tira o operador da listagem.
 */
@Component({
  selector: 'app-product-form-dialog',
  imports: [DatePipe, InlineAlertComponent, ModalShellComponent, ReactiveFormsModule],
  template: `
    <app-modal-shell
      [title]="editing ? 'Editar produto' : 'Novo produto'"
      confirmLabel="Salvar"
      dismissLabel="Sair"
      busyLabel="Salvando…"
      [busy]="saving"
      (confirm)="submit()"
      (dismiss)="close()"
    >
      <form class="nf-form-grid" [formGroup]="form" novalidate (ngSubmit)="submit()">
        <div class="nf-field" [class.invalid]="invalid('code')">
          <label class="nf-label" for="product-code">Código<span class="req">*</span></label>
          <input
            id="product-code"
            class="nf-input"
            formControlName="code"
            maxlength="64"
            autocomplete="off"
            placeholder="Ex.: TEC-001"
            (blur)="normalizeCode()"
          />
          @if (invalid('code')) {
            <span class="nf-error">Informe o código.</span>
          } @else {
            <span class="nf-hint">Único no catálogo, até 64 caracteres</span>
          }
        </div>

        <!-- Descricao ocupa a linha: e o campo mais longo do formulario. -->
        <div class="nf-field nf-field--full" [class.invalid]="invalid('description')">
          <label class="nf-label" for="product-description">Descrição<span class="req">*</span></label>
          <input
            id="product-description"
            class="nf-input"
            formControlName="description"
            maxlength="200"
            autocomplete="off"
            placeholder="Ex.: Teclado mecânico"
          />
          @if (invalid('description')) {
            <span class="nf-error">Informe a descrição.</span>
          } @else {
            <span class="nf-hint">{{ form.controls.description.value.length }}/200</span>
          }
        </div>

        <div class="nf-field nf-field--full checkbox-field">
          <label class="check">
            <input type="checkbox" formControlName="tracksStock" />
            <span>Controla estoque</span>
          </label>
          <span class="nf-hint">
            Desmarcado, o produto entra nas notas sem validar nem movimentar saldo —
            use para serviços e itens sob encomenda.
          </span>
        </div>

        @if (!editing && form.controls.tracksStock.value) {
          <div class="nf-field" [class.invalid]="invalid('balance')">
            <label class="nf-label" for="product-balance">Saldo inicial<span class="req">*</span></label>
            <input
              id="product-balance"
              class="nf-input"
              type="number"
              formControlName="balance"
              inputmode="numeric"
              min="0"
              max="999999999"
            />
            @if (invalid('balance')) {
              <span class="nf-error">Use um inteiro entre 0 e 999.999.999.</span>
            } @else {
              <span class="nf-hint">Inteiro maior ou igual a zero</span>
            }
          </div>
        }
        @if (editing) {
          <p class="nf-hint">O saldo não é editado aqui: alterações físicas exigem um ajuste auditável.</p>

          <section class="audit-summary" aria-label="Dados de auditoria">
            <div>
              <span>Criado em</span>
              <strong>{{ product!.createdAt | date: 'dd/MM/yyyy HH:mm' }} · {{ product!.createdBy ?? 'sistema' }}</strong>
            </div>
            <div>
              <span>Última edição</span>
              <strong>{{ product!.updatedAt | date: 'dd/MM/yyyy HH:mm' }} · {{ product!.updatedBy ?? product!.createdBy ?? 'sistema' }}</strong>
            </div>
          </section>

          @if (product!.auditEvents?.length) {
            <section class="audit-trail" aria-labelledby="product-audit-title">
              <h3 id="product-audit-title">Histórico</h3>
              <ul>
                @for (event of product!.auditEvents!; track event.occurredAt + event.type) {
                  <li>
                    <strong>{{ auditLabel(event.type) }}</strong>
                    <span>{{ event.actorName }} · {{ event.occurredAt | date: 'dd/MM/yyyy HH:mm' }}</span>
                  </li>
                }
              </ul>
            </section>
          }
        }

        @if (error) {
          <div class="nf-field--full">
            <app-inline-alert
            tone="error"
            [title]="error.title"
            [message]="error.message"
            [traceId]="error.traceId"
          />
          </div>
        }

        <button type="submit" hidden></button>
      </form>
    </app-modal-shell>
  `,
  styles: `


    .check {
      display: flex;
      align-items: center;
      color: var(--n-700);
      font-size: var(--fs-md);
      gap: var(--sp-2);
      cursor: pointer;
    }

    .check input {
      width: 16px;
      height: 16px;
      accent-color: var(--brand-600);
    }

    .checkbox-field {
      gap: var(--sp-1);
    }

    .audit-summary {
      display: grid;
      gap: var(--sp-2);
      padding: var(--sp-3);
      border: 1px solid var(--n-200);
      border-radius: var(--radius-md);
      background: var(--n-50);
    }

    .audit-summary div,
    .audit-trail li {
      display: flex;
      justify-content: space-between;
      gap: var(--sp-3);
      font-size: var(--fs-sm);
    }

    .audit-summary span,
    .audit-trail span {
      color: var(--n-600);
    }

    .audit-trail h3 {
      margin: 0 0 var(--sp-2);
      font-size: var(--fs-md);
    }

    .audit-trail ul {
      display: grid;
      gap: var(--sp-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class ProductFormDialog {
  private readonly dialogRef = inject<MatDialogRef<ProductFormDialog, Product>>(MatDialogRef);
  private readonly formBuilder = inject(FormBuilder);
  private readonly productService = inject(ProductService);
  private readonly apiError = inject(ApiErrorService);
  private readonly notification = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly data = inject<{ product?: Product } | null>(MAT_DIALOG_DATA, { optional: true });
  readonly product = this.data?.product;
  readonly editing = !!this.data?.product;

  readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(64)]],
    description: ['', [Validators.required, Validators.maxLength(200)]],
    tracksStock: [true],
    balance: [
      0,
      [
        Validators.required,
        Validators.min(0),
        Validators.max(999_999_999),
        Validators.pattern(/^\d+$/),
      ],
    ],
  });

  saving = false;
  error: UiError | null = null;

  constructor() {
    const product = this.data?.product;
    if (product) this.form.patchValue({ code: product.code, description: product.description, tracksStock: product.tracksStock, balance: product.balance });
  }

  /** Vermelho só depois de tocar no campo. */
  invalid(name: 'code' | 'description' | 'balance'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  close(): void {
    if (!this.saving) this.dialogRef.close();
  }

  normalizeCode(): void {
    this.form.controls.code.setValue(this.form.controls.code.value.trim().toUpperCase());
  }

  auditLabel(type: 'Created' | 'Edited'): string {
    return type === 'Created' ? 'Criado' : 'Editado';
  }

  submit(): void {
    this.normalizeCode();
    this.form.controls.description.setValue(this.form.controls.description.value.trim());
    this.form.markAllAsTouched();

    if (this.form.invalid || this.saving) return;

    const raw = this.form.getRawValue();
    this.saving = true;
    this.error = null;
    const product = this.data?.product;
    const request$ = product
      ? this.productService.update(product.id, { code: raw.code, description: raw.description, tracksStock: raw.tracksStock }, product.version ?? '')
      : this.productService.create({ code: raw.code, description: raw.description, balance: raw.tracksStock ? raw.balance : 0, tracksStock: raw.tracksStock });
    request$
      .pipe(
        finalize(() => (this.saving = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (product) => {
          this.notification.success(
            this.editing ? 'Produto atualizado' : 'Produto cadastrado',
            `${product.code} — ${product.description}`,
          );
          this.dialogRef.close(product);
        },
        error: (error: unknown) => (this.error = this.apiError.from(error)),
      });
  }
}
