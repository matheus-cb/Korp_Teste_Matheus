import { ChangeDetectionStrategy, Component, DestroyRef, inject, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import type { AiDraft, AiDraftItem } from '../core/models/ai-draft.model';
import type { UiError } from '../core/models/api.models';
import { AiDraftService } from '../core/services/ai-draft.service';
import { ApiErrorService } from '../core/services/api-error.service';
import { DraftTransferService } from '../core/services/draft-transfer.service';
import { NotificationService } from '../core/services/notification.service';
import { InlineAlertComponent } from '../shared/inline-alert.component';

interface Turn {
  role: 'user' | 'assistant';
  text: string;
  draft?: AiDraft;
}

const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const ACCEPTED = ['image/jpeg', 'image/png', 'image/webp'];

/**
 * Assistente como painel lateral, disponível em qualquer tela. Ele apenas
 * consulta o catálogo e monta um rascunho — quem cria a nota é a pessoa,
 * revisando o resultado no formulário de emissão.
 */
@Component({
  selector: 'app-agent-panel',
  imports: [FormsModule, InlineAlertComponent, MatIconModule, MatProgressSpinnerModule],
  template: `
    <aside class="agent" aria-label="Assistente">
      <header class="agent-head">
        <mat-icon class="brand-icon" svgIcon="sparkles" aria-hidden="true" />
        <strong>Assistente</strong>
        <span class="tag">rascunho</span>
        <button type="button" class="icon-btn" aria-label="Fechar assistente" (click)="closed.emit()">
          <mat-icon svgIcon="x" />
        </button>
      </header>

      <div class="chips">
        <button type="button" (click)="useSuggestion('Duas unidades do cabo USB-C e um teclado sem fio')">
          Criar nota por texto
        </button>
        <button type="button" (click)="pickImage()">
          <mat-icon svgIcon="image" aria-hidden="true" />
          Ler de uma foto
        </button>
        <button type="button" (click)="useSuggestion('Confira a disponibilidade do monitor 24 polegadas')">
          Conferir disponibilidade
        </button>
      </div>

      <div class="body">
        @if (turns.length === 0) {
          <p class="hint">
            Descreva o pedido em texto ou envie uma foto. O assistente procura os
            produtos no catálogo e monta um rascunho para você revisar.
          </p>
        }

        @for (turn of turns; track $index) {
          @if (turn.role === 'user') {
            <div class="msg user">{{ turn.text }}</div>
          } @else {
            <div class="msg assistant">
              {{ turn.text }}

              @if (turn.draft && turn.draft.items.length) {
                <div class="draft">
                  <div class="draft-head">Rascunho — não salvo</div>
                  @for (item of turn.draft.items; track item.productId) {
                    <div class="draft-row">
                      <span class="draft-name">{{ item.description }}</span>
                      <span
                        class="status-badge"
                        [class.status-done]="item.availability === 'available'"
                        [class.status-rejected]="item.availability === 'insufficient'"
                        [class.status-pending]="item.availability !== 'available' && item.availability !== 'insufficient'"
                      >
                        {{ availabilityLabel(item) }}
                      </span>
                      <b>{{ item.quantity }}</b>
                    </div>
                  }
                  <div class="draft-actions">
                    <button type="button" class="nf-btn" (click)="discard(turn)">Descartar</button>
                    <button type="button" class="nf-btn nf-btn--primary" (click)="review(turn.draft)">
                      Revisar e criar
                    </button>
                  </div>
                </div>
              }

              @if (turn.draft?.unresolvedItems?.length) {
                <ul class="unresolved">
                  @for (item of turn.draft!.unresolvedItems; track $index) {
                    <li>{{ item.description }} — {{ item.reason }}</li>
                  }
                </ul>
              }

              @if (turn.draft?.steps?.length) {
                <div class="trace">
                  @for (step of turn.draft!.steps; track $index) {
                    <span>· {{ step.summary }}</span>
                  }
                </div>
              }
            </div>
          }
        }

        @if (loading) {
          <div class="msg assistant loading">
            <mat-spinner diameter="16" /> Consultando o catálogo…
          </div>
        }

        @if (error) {
          <app-inline-alert tone="error" [title]="error.title" [message]="error.message" [traceId]="error.traceId" />
        }
      </div>

      <p class="guard">
        O assistente só consulta o catálogo. Não cria nota, não fecha nota e não altera saldo.
      </p>

      <form class="composer" (ngSubmit)="send()">
        @if (image) {
          <div class="attachment">
            <mat-icon svgIcon="image" aria-hidden="true" />
            <span>{{ image.name }}</span>
            <button type="button" class="icon-btn" aria-label="Remover imagem" (click)="image = null">
              <mat-icon svgIcon="x" />
            </button>
          </div>
        }
        <textarea
          rows="2"
          maxlength="1000"
          placeholder="Descreva o pedido ou anexe uma foto…"
          aria-label="Mensagem para o assistente"
          [(ngModel)]="prompt"
          [ngModelOptions]="{ standalone: true }"
          (keydown.enter)="onEnter($event)"
        ></textarea>
        <div class="composer-actions">
          <button type="button" class="icon-btn" aria-label="Anexar imagem" (click)="pickImage()">
            <mat-icon svgIcon="paperclip" />
          </button>
          <button
            type="submit"
            class="send"
            aria-label="Enviar"
            [disabled]="loading || (!prompt.trim() && !image)"
          >
            <mat-icon svgIcon="arrow-up" />
          </button>
        </div>
        <input
          #fileInput
          type="file"
          hidden
          aria-hidden="true"
          tabindex="-1"
          aria-label="Selecionar imagem"
          accept="image/jpeg,image/png,image/webp"
          (change)="onFile($event)"
        />
      </form>
    </aside>
  `,
  styleUrl: './agent-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.Default,
})
export class AgentPanelComponent {
  readonly closed = output<void>();

  private readonly aiDraft = inject(AiDraftService);
  private readonly apiError = inject(ApiErrorService);
  private readonly transfer = inject(DraftTransferService);
  private readonly notification = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  turns: Turn[] = [];
  prompt = '';
  image: File | null = null;
  loading = false;
  error: UiError | null = null;

  availabilityLabel(item: AiDraftItem): string {
    switch (item.availability) {
      case 'available':
        return 'disponível';
      case 'insufficient':
        return `saldo ${item.availableBalance ?? 0}`;
      default:
        return 'a validar';
    }
  }

  useSuggestion(text: string): void {
    this.prompt = text;
  }

  pickImage(): void {
    const input = document.querySelector<HTMLInputElement>('app-agent-panel input[type=file]');
    input?.click();
  }

  onFile(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    // Mesmas regras do backend, para o erro aparecer antes da viagem.
    if (!ACCEPTED.includes(file.type)) {
      this.notification.warning('Formato não aceito', 'Envie uma imagem JPEG, PNG ou WebP.');
      return;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      this.notification.warning('Imagem muito grande', 'O limite é 5 MB por arquivo.');
      return;
    }
    this.image = file;
  }

  onEnter(event: Event): void {
    const keyboard = event as KeyboardEvent;
    if (keyboard.shiftKey) return;
    event.preventDefault();
    this.send();
  }

  send(): void {
    const text = this.prompt.trim();
    if ((!text && !this.image) || this.loading) return;

    this.turns = [...this.turns, { role: 'user', text: text || 'Imagem enviada' }];
    this.loading = true;
    this.error = null;
    const image = this.image ?? undefined;
    this.prompt = '';
    this.image = null;

    this.aiDraft
      .create(text, image)
      .pipe(
        finalize(() => (this.loading = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (draft) => {
          const resolved = draft.items.length;
          const summary = resolved
            ? `Encontrei ${resolved} ${resolved === 1 ? 'produto' : 'produtos'} no catálogo.`
            : 'Não consegui identificar produtos do catálogo nesse pedido.';
          this.turns = [...this.turns, { role: 'assistant', text: summary, draft }];
        },
        error: (error: unknown) => (this.error = this.apiError.from(error)),
      });
  }

  discard(turn: Turn): void {
    this.turns = this.turns.filter((candidate) => candidate !== turn);
  }

  /** O rascunho vai para o formulário de emissão; quem cria a nota é a pessoa. */
  review(draft: AiDraft | undefined): void {
    if (!draft) return;
    this.transfer.set(draft.items);
    this.closed.emit();
    void this.router.navigate(['/notas'], { queryParams: { novo: 1 } });
  }
}
