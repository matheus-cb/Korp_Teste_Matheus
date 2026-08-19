import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  template: `
    <section class="empty">
      <span aria-hidden="true">{{ badge() }}</span>
      <h2>{{ title() }}</h2>
      <p>{{ description() }}</p>
      <ng-content />
    </section>
  `,
  styles: `
    .empty {
      padding: 3rem 1.25rem;
      color: #60766f;
      text-align: center;
    }
    span {
      display: grid;
      width: 54px;
      height: 54px;
      margin: 0 auto 0.9rem;
      border: 1px solid #cae0da;
      border-radius: 17px;
      place-items: center;
      color: #176b5f;
      background: #eaf5f2;
      font-size: 0.78rem;
      font-weight: 850;
      letter-spacing: 0.04em;
    }
    h2 {
      margin: 0;
      color: #294a43;
      font-size: 1.05rem;
    }
    p {
      max-width: 430px;
      margin: 0.45rem auto 1rem;
      font-size: 0.84rem;
      line-height: 1.5;
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class EmptyStateComponent {
  readonly badge = input('—');
  readonly title = input.required<string>();
  readonly description = input.required<string>();
}
