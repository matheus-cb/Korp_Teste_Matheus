import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { finalize } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { UiError } from '../../core/models/api.models';
import { ApiErrorService } from '../../core/services/api-error.service';
import { NotificationService } from '../../core/services/notification.service';
import { InlineAlertComponent } from '../../shared/inline-alert.component';
import { ModalShellComponent } from '../../shared/modal-shell.component';

interface ImportRowError {
  reference: string | null;
  line: number;
  code: string;
  message: string;
}

interface ImportResult {
  importId: string;
  createdInvoices: number;
  errorCount: number;
  errors: ImportRowError[];
  alreadyImported: boolean;
  message?: string | null;
}

const MAX_BYTES = 2 * 1024 * 1024;

/** Importação de notas por CSV: ação dentro da listagem, portanto em modal. */
@Component({
  selector: 'app-import-invoices-dialog',
  imports: [InlineAlertComponent, MatIconModule, ModalShellComponent],
  template: `
    <app-modal-shell
      title="Importar notas"
      [confirmLabel]="result ? 'Concluir' : 'Importar'"
      dismissLabel="Sair"
      busyLabel="Importando…"
      [busy]="importing"
      [canConfirm]="!!file || !!result"
      (confirm)="submit()"
      (dismiss)="close()"
    >
      @if (!result) {
        <section class="formato">
          <strong>Formato esperado</strong>
          <p>
            Uma linha por item, agrupadas pela coluna <code>nota</code>. Aceita
            <code>;</code> ou <code>,</code> como separador.
          </p>
          <pre>nota;codigo;quantidade
1;CAB-USBC-2M;2
1;TEC-SF-01;1
2;MON-24-IPS;3</pre>
        </section>

        <div class="nf-field">
          <label class="nf-label" for="import-file">Arquivo CSV</label>
          <input
            id="import-file"
            class="nf-input file"
            type="file"
            accept=".csv,text/csv"
            (change)="onFile($event)"
          />
          <span class="nf-hint">Até 2 MB e 2.000 linhas.</span>
        </div>

        <label class="check">
          <input type="checkbox" [checked]="closeAfter" (change)="toggleClose($event)" />
          <span>Fechar as notas após importar</span>
        </label>
        <p class="nf-hint">
          Sem marcar, as notas entram como <strong>Abertas</strong> e você confere antes
          de dar baixa no estoque.
        </p>

        @if (error) {
          <app-inline-alert
            tone="error"
            [title]="error.title"
            [message]="error.message"
            [traceId]="error.traceId"
          />
        }
      } @else {
        <section class="resultado">
          @if (result.alreadyImported) {
            <div class="status-badge status-open">Arquivo já importado</div>
            <p>{{ result.message }}</p>
          } @else {
            <div class="status-badge status-done">
              {{ result.createdInvoices }}
              {{ result.createdInvoices === 1 ? 'nota criada' : 'notas criadas' }}
            </div>
          }

          @if (result.errors.length) {
            <h3>{{ result.errors.length }} linha(s) com problema</h3>
            <table class="data-table">
              <thead>
                <tr>
                  <th scope="col">Linha</th>
                  <th scope="col">Nota</th>
                  <th scope="col">Motivo</th>
                </tr>
              </thead>
              <tbody>
                @for (row of result.errors; track $index) {
                  <tr>
                    <td class="numeric">{{ row.line || '—' }}</td>
                    <td>{{ row.reference ?? '—' }}</td>
                    <td>{{ row.message }}</td>
                  </tr>
                }
              </tbody>
            </table>
            <p class="nf-hint">
              As demais linhas foram importadas normalmente — falha parcial não
              cancela o arquivo inteiro.
            </p>
          }
        </section>
      }
    </app-modal-shell>
  `,
  styles: `
    .formato {
      margin-bottom: var(--sp-4);
      padding: var(--sp-3) var(--sp-4);
      border: 1px solid var(--n-200);
      border-radius: var(--r-md);
      background: var(--n-25);
    }

    .formato p {
      margin: var(--sp-1) 0 var(--sp-2);
      color: var(--n-600);
      font-size: var(--fs-sm);
    }

    .formato pre {
      margin: 0;
      padding: var(--sp-2);
      overflow-x: auto;
      border-radius: var(--r-sm);
      color: var(--n-700);
      background: var(--n-0);
      font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
      font-size: var(--fs-sm);
    }

    .file {
      height: auto;
      padding: 6px var(--sp-2);
    }

    .check {
      display: flex;
      align-items: center;
      margin-top: var(--sp-4);
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

    .resultado h3 {
      margin: var(--sp-4) 0 var(--sp-2);
      font-size: var(--fs-md);
      font-weight: 640;
    }

    .resultado p {
      margin: var(--sp-2) 0 0;
      color: var(--n-600);
      font-size: var(--fs-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class ImportInvoicesDialog {
  private readonly dialogRef = inject<MatDialogRef<ImportInvoicesDialog, boolean>>(MatDialogRef);
  private readonly http = inject(HttpClient);
  private readonly apiError = inject(ApiErrorService);
  private readonly notification = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  file: File | null = null;
  closeAfter = false;
  importing = false;
  error: UiError | null = null;
  result: ImportResult | null = null;

  close(): void {
    if (!this.importing) this.dialogRef.close(!!this.result);
  }

  toggleClose(event: Event): void {
    this.closeAfter = (event.target as HTMLInputElement).checked;
  }

  onFile(event: Event): void {
    const picked = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.error = null;

    if (picked && picked.size > MAX_BYTES) {
      this.notification.warning('Arquivo muito grande', 'O limite é 2 MB.');
      this.file = null;
      return;
    }

    this.file = picked;
  }

  submit(): void {
    if (this.result) {
      this.dialogRef.close(true);
      return;
    }
    if (!this.file || this.importing) return;

    const data = new FormData();
    data.append('file', this.file, this.file.name);
    data.append('close', String(this.closeAfter));

    this.importing = true;
    this.error = null;
    this.http
      .post<ImportResult>(`${environment.billingApiUrl}/invoices/import`, data)
      .pipe(
        finalize(() => (this.importing = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.result = result;
          if (result.alreadyImported) {
            this.notification.info('Arquivo já importado', 'Nada foi duplicado.');
            return;
          }
          this.notification.success(
            'Importação concluída',
            `${result.createdInvoices} nota(s) criada(s), ${result.errorCount} erro(s).`,
          );
        },
        error: (error: unknown) => (this.error = this.apiError.from(error)),
      });
  }
}
