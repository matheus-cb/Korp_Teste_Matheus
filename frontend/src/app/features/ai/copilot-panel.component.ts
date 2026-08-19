import { ChangeDetectionStrategy, Component, DestroyRef, inject, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { finalize } from 'rxjs';
import type { AiDraft, AiDraftItem } from '../../core/models/ai-draft.model';
import type { UiError } from '../../core/models/api.models';
import { AiDraftService } from '../../core/services/ai-draft.service';
import { ApiErrorService } from '../../core/services/api-error.service';
import { InlineAlertComponent } from '../../shared/inline-alert.component';

@Component({
  selector: 'app-copilot-panel',
  imports: [
    FormsModule,
    InlineAlertComponent,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  template: `
    <section class="copilot-card" aria-labelledby="copilot-title">
      <div class="hero">
        <div class="ai-mark" aria-hidden="true">IA</div>
        <div>
          <p>Copiloto de faturamento</p>
          <h2 id="copilot-title">Transforme um pedido em rascunho</h2>
          <span>A IA consulta o catálogo por MCP. Você revisa e confirma tudo.</span>
        </div>
      </div>

      <div class="guardrail">
        <span aria-hidden="true">✓</span>
        <p><strong>Leitura somente.</strong> O Copiloto não cria notas, não fecha documentos e não altera o estoque.</p>
      </div>

      <div class="prompt-area">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Descreva o pedido</mat-label>
          <textarea
            matInput
            [(ngModel)]="instruction"
            maxlength="2000"
            rows="5"
            placeholder="Ex.: Separe dois teclados mecânicos e três mouses sem fio."
            [disabled]="loading"
          ></textarea>
          <mat-hint>Use código, descrição e quantidade — pode escrever naturalmente.</mat-hint>
          <mat-hint align="end">{{ instruction.length }}/2000</mat-hint>
        </mat-form-field>

        <div class="image-input">
          <div>
            <strong>Imagem do pedido <span>opcional</span></strong>
            <small>JPEG, PNG ou WebP · até 5 MB</small>
          </div>
          <input
            #fileInput
            class="visually-hidden"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            [disabled]="loading"
            (change)="selectFile($event)"
          />
          @if (selectedFile) {
            <div class="selected-file">
              <span class="file-mark" aria-hidden="true">IMG</span>
              <span><strong>{{ selectedFile.name }}</strong><small>{{ formatBytes(selectedFile.size) }}</small></span>
              <button mat-button type="button" [disabled]="loading" (click)="removeFile(fileInput)">Remover</button>
            </div>
          } @else {
            <button mat-stroked-button type="button" [disabled]="loading" (click)="fileInput.click()">
              Selecionar imagem
            </button>
          }
        </div>

        <p class="privacy-note">
          Não envie dados pessoais ou sigilosos. Texto e imagem são processados pelo provedor de IA conforme a política de retenção documentada.
        </p>

        @if (fileError) {
          <p class="field-error" role="alert">{{ fileError }}</p>
        }

        <button
          mat-flat-button
          type="button"
          class="generate-button"
          [disabled]="loading || (!instruction.trim() && !selectedFile)"
          (click)="generate()"
        >
          {{ loading ? 'Interpretando pedido…' : 'Interpretar com IA' }}
        </button>
      </div>

      @if (loading) {
        <div class="processing" role="status" aria-live="polite">
          <mat-progress-bar mode="indeterminate" />
          <div class="processing-copy">
            <strong>Preparando o rascunho</strong>
            <span>O Copiloto está consultando o catálogo e validando os resultados.</span>
          </div>
          <ol>
            <li class="done"><span>1</span> Pedido recebido</li>
            <li class="active"><span>2</span> Consulta MCP em andamento</li>
            <li><span>3</span> Validação do rascunho</li>
          </ol>
        </div>
      }

      @if (error) {
        <app-inline-alert
          class="result-alert"
          tone="error"
          [title]="error.title"
          [message]="error.message"
          [traceId]="error.traceId"
          [retryable]="true"
          (retry)="generate()"
        />
      }

      @if (draft) {
        <section class="result" aria-live="polite">
          <div class="result-heading">
            <div>
              <p>Rascunho gerado</p>
              <h3>Revise antes de aplicar</h3>
            </div>
            <span>{{ draft.items.length }} {{ draft.items.length === 1 ? 'item reconhecido' : 'itens reconhecidos' }}</span>
          </div>

          @if (draft.steps.length) {
            <details class="trace">
              <summary>Ver consultas realizadas</summary>
              <ol>
                @for (step of draft.steps; track $index) {
                  <li [class.failed]="step.status === 'failed'">
                    <span aria-hidden="true">{{ step.status === 'failed' ? '!' : '✓' }}</span>
                    <div><strong>{{ toolLabel(step.tool) }}</strong><small>{{ step.summary }}</small></div>
                  </li>
                }
              </ol>
              <p>Esta trilha mostra ações e resultados factuais — nunca o raciocínio interno do modelo.</p>
            </details>
          }

          @if (draft.items.length) {
            <div class="draft-items">
              @for (item of draft.items; track item.productId) {
                <article>
                  <span class="quantity">{{ item.quantity }}×</span>
                  <div>
                    <strong>{{ item.description }}</strong>
                    <small>{{ item.code }}</small>
                  </div>
                  <span class="availability" [class.insufficient]="item.availability === 'insufficient'">
                    {{ availabilityLabel(item) }}
                  </span>
                </article>
              }
            </div>
          }

          @if (draft.unresolvedItems.length) {
            <div class="unresolved">
              <strong>{{ draft.unresolvedItems.length }} {{ draft.unresolvedItems.length === 1 ? 'item precisa' : 'itens precisam' }} de revisão</strong>
              @for (item of draft.unresolvedItems; track item.description) {
                <p>“{{ item.description }}” — {{ item.reason }}</p>
              }
            </div>
          }

          @for (warning of draft.warnings; track warning) {
            <app-inline-alert tone="warning" title="Atenção" [message]="warning" />
          }

          <div class="result-actions">
            <button mat-stroked-button type="button" (click)="clearResult()">Descartar</button>
            <button
              mat-flat-button
              type="button"
              [disabled]="draft.items.length === 0"
              (click)="apply()"
            >
              Aplicar ao formulário
            </button>
          </div>
        </section>
      }
    </section>
  `,
  styleUrl: './copilot-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.Default,
})
export class CopilotPanelComponent {
  private readonly aiDraftService = inject(AiDraftService);
  private readonly apiError = inject(ApiErrorService);
  private readonly destroyRef = inject(DestroyRef);

  readonly applied = output<AiDraftItem[]>();

  instruction = '';
  selectedFile: File | null = null;
  fileError = '';
  loading = false;
  error: UiError | null = null;
  draft: AiDraft | null = null;

  selectFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    this.fileError = '';
    if (!file) return;

    void this.validateImage(file).then((message) => {
      if (message) {
        this.fileError = message;
        input.value = '';
        this.selectedFile = null;
      } else {
        this.selectedFile = file;
      }
    });
  }

  removeFile(input: HTMLInputElement): void {
    input.value = '';
    this.selectedFile = null;
    this.fileError = '';
  }

  generate(): void {
    if (this.loading || (!this.instruction.trim() && !this.selectedFile)) return;
    this.loading = true;
    this.error = null;
    this.draft = null;
    this.aiDraftService
      .create(this.instruction, this.selectedFile ?? undefined)
      .pipe(
        finalize(() => (this.loading = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (draft) => (this.draft = this.normalizeDraft(draft)),
        error: (error: unknown) => (this.error = this.apiError.from(error)),
      });
  }

  apply(): void {
    if (!this.draft?.items.length) return;
    this.applied.emit(this.draft.items);
  }

  clearResult(): void {
    this.draft = null;
    this.error = null;
  }

  formatBytes(bytes: number): string {
    return bytes < 1_000_000
      ? `${Math.ceil(bytes / 1000)} KB`
      : `${(bytes / 1_000_000).toFixed(1)} MB`;
  }

  toolLabel(tool: string): string {
    const labels: Record<string, string> = {
      search_products: 'Pesquisa de produtos',
      get_product: 'Consulta de produto',
      check_availability: 'Verificação de disponibilidade',
    };
    return labels[tool] ?? 'Consulta ao catálogo';
  }

  availabilityLabel(item: AiDraftItem): string {
    if (item.availability === 'available') return 'Disponível agora';
    if (item.availability === 'insufficient') return 'Saldo insuficiente';
    return 'Validar no fechamento';
  }

  private normalizeDraft(draft: AiDraft): AiDraft {
    return {
      runId: draft.runId,
      items: draft.items ?? [],
      unresolvedItems: draft.unresolvedItems ?? [],
      warnings: draft.warnings ?? [],
      steps: draft.steps ?? [],
    };
  }

  private async validateImage(file: File): Promise<string> {
    const allowedTypes = ['image/jpeg', 'image/png', 'image/webp'];
    if (!allowedTypes.includes(file.type)) {
      return 'Formato não aceito. Selecione uma imagem JPEG, PNG ou WebP.';
    }
    if (file.size > 5_000_000) {
      return 'A imagem excede o limite de 5 MB.';
    }

    if ('createImageBitmap' in window) {
      try {
        const bitmap = await createImageBitmap(file);
        const pixels = bitmap.width * bitmap.height;
        bitmap.close();
        if (pixels > 12_000_000) return 'A imagem excede o limite de 12 megapixels.';
      } catch {
        return 'Não foi possível ler a imagem selecionada.';
      }
    }
    return '';
  }
}
