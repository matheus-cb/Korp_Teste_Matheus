import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule],
  template: `
    <main class="screen">
      <section class="card">
        <header class="head">
          <span class="mark" aria-hidden="true">N</span>
          <div>
            <h1>NotaFlow</h1>
            <p>Estoque e faturamento em um só fluxo</p>
          </div>
        </header>

        <form [formGroup]="form" novalidate (ngSubmit)="submit()">
          <div class="nf-field" [class.invalid]="invalid('userName')">
            <label class="nf-label" for="userName">Usuário</label>
            <input
              id="userName"
              class="nf-input"
              formControlName="userName"
              autocomplete="username"
              autocapitalize="none"
              spellcheck="false"
            />
          </div>

          <div class="nf-field" [class.invalid]="invalid('password')">
            <label class="nf-label" for="password">Senha</label>
            <input
              id="password"
              class="nf-input"
              type="password"
              formControlName="password"
              autocomplete="current-password"
            />
          </div>

          @if (error) {
            <p class="error" role="alert">{{ error }}</p>
          }

          <button type="submit" class="nf-btn nf-btn--primary submit" [disabled]="loading">
            {{ loading ? 'Entrando…' : 'Entrar' }}
          </button>
        </form>

        <footer class="demo">
          <strong>Ambiente demonstrativo</strong>
          <p class="demo-hint">Entre com um clique. Senha de todos: <code>notaflow123</code></p>
          <div class="demo-actions">
            @for (conta of contasDemo; track conta.userName) {
              <button
                type="button"
                class="nf-btn demo-btn"
                [disabled]="loading"
                (click)="entrarComo(conta)"
              >
                {{ conta.rotulo }}
                <small>{{ conta.userName }}</small>
              </button>
            }
          </div>
        </footer>
      </section>
    </main>
  `,
  styles: `
    .screen {
      display: grid;
      min-height: 100dvh;
      padding: var(--sp-4);
      background: var(--n-100);
      place-items: center;
    }

    .card {
      width: 100%;
      max-width: 380px;
      overflow: hidden;
      border: 1px solid var(--n-200);
      border-radius: var(--r-md);
      background: var(--n-0);
      box-shadow: var(--shadow-md);
    }

    .head {
      display: flex;
      align-items: center;
      padding: var(--sp-5) var(--sp-5) var(--sp-4);
      border-bottom: 1px solid var(--n-200);
      gap: var(--sp-3);
    }

    .mark {
      display: grid;
      width: 34px;
      height: 34px;
      border-radius: var(--r-sm);
      color: #ffffff;
      background: var(--brand-600);
      font-size: var(--fs-lg);
      font-weight: 700;
      place-items: center;
    }

    h1 {
      margin: 0;
      color: var(--n-900);
      font-size: var(--fs-xl);
      font-weight: 660;
    }

    .head p {
      margin: 2px 0 0;
      color: var(--n-500);
      font-size: var(--fs-sm);
    }

    form {
      display: flex;
      flex-direction: column;
      padding: var(--sp-5);
      gap: var(--sp-4);
    }

    .submit {
      height: 36px;
      justify-content: center;
    }

    .error {
      margin: 0;
      padding: var(--sp-2) var(--sp-3);
      border-radius: var(--r-sm);
      color: var(--st-rejected-fg);
      background: var(--st-rejected-bg);
      font-size: var(--fs-sm);
    }

    .demo {
      display: flex;
      flex-direction: column;
      padding: var(--sp-3) var(--sp-5) var(--sp-5);
      border-top: 1px solid var(--n-100);
      color: var(--n-500);
      font-size: var(--fs-sm);
      gap: 2px;
    }

    .demo strong {
      color: var(--n-600);
      font-size: var(--fs-xs);
      font-weight: 660;
      letter-spacing: 0.06em;
      text-transform: uppercase;
    }

    .demo-hint {
      margin: 2px 0 var(--sp-2);
    }

    .demo-hint code {
      color: var(--n-600);
      font-size: var(--fs-sm);
    }

    .demo-actions {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--sp-2);
    }

    .demo-btn {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: var(--sp-2);
      border: 1px solid var(--n-200);
      background: #fff;
      color: var(--n-600);
      font-weight: 600;
      gap: 1px;
      line-height: 1.2;
    }

    .demo-btn small {
      color: var(--n-500);
      font-size: var(--fs-xs);
      font-weight: 400;
    }

    .demo-btn:hover:not(:disabled) {
      border-color: var(--n-300);
      background: var(--n-50);
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class LoginPage {
  /**
   * Contas de demonstracao. Ficam visiveis de proposito, inclusive na instancia
   * publicada: o NotaFlow e demonstrativo, nao guarda dado real, e esconder a
   * credencial so atrapalharia quem vem conhecer o fluxo. Se um dia a aplicacao
   * passar a valer, isto sai junto com o seeding.
   */
  protected readonly contasDemo = [
    { rotulo: 'Operador', userName: 'operador', password: 'notaflow123' },
    { rotulo: 'Supervisor', userName: 'supervisor', password: 'notaflow123' },
  ] as const;

  private readonly formBuilder = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly form = this.formBuilder.nonNullable.group({
    userName: ['', [Validators.required, Validators.minLength(3)]],
    password: ['', [Validators.required, Validators.minLength(6)]],
  });

  loading = false;
  error = '';

  /** Preenche o formulario com uma conta de demonstracao e ja entra. */
  entrarComo(conta: { userName: string; password: string }): void {
    if (this.loading) return;
    this.form.setValue({ userName: conta.userName, password: conta.password });
    this.submit();
  }

  invalid(name: 'userName' | 'password'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.loading) return;

    const { userName, password } = this.form.getRawValue();
    this.loading = true;
    this.error = '';
    this.auth
      .signIn(userName, password)
      .pipe(
        finalize(() => (this.loading = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => void this.router.navigate(['/']),
        error: () => (this.error = 'Usuário ou senha inválidos.'),
      });
  }
}
