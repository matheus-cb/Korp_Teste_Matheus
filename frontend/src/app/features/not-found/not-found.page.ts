import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found-page',
  imports: [MatButtonModule, RouterLink],
  template: `
    <section class="not-found">
      <span>404</span>
      <p>Página não encontrada</p>
      <h1>Este caminho não faz parte da operação.</h1>
      <small>Volte à visão geral ou consulte as notas fiscais.</small>
      <div>
        <a mat-flat-button routerLink="/">Ir para a visão geral</a>
        <a mat-button routerLink="/notas">Ver notas</a>
      </div>
    </section>
  `,
  styles: `
    .not-found {
      display: grid;
      min-height: 62vh;
      padding: 2rem;
      place-content: center;
      text-align: center;
    }
    .not-found > span {
      display: grid;
      width: 72px;
      height: 72px;
      margin: 0 auto 1rem;
      border-radius: 22px;
      place-items: center;
      color: #176c60;
      background: #dff1ec;
      font-size: 1rem;
      font-weight: 850;
    }
    p {
      margin: 0;
      color: #16806f;
      font-size: 0.7rem;
      font-weight: 800;
      letter-spacing: 0.1em;
      text-transform: uppercase;
    }
    h1 {
      max-width: 510px;
      margin: 0.45rem auto 0;
      color: #214a41;
      font-size: clamp(1.5rem, 4vw, 2.2rem);
      letter-spacing: -0.04em;
    }
    small {
      margin: 0.65rem 0 1.25rem;
      color: #6d827b;
      font-size: 0.84rem;
    }
    div {
      display: flex;
      gap: 0.5rem;
      justify-content: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class NotFoundPage {}
