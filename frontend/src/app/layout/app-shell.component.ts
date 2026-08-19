import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { AgentPanelComponent } from './agent-panel.component';

interface NavigationItem {
  label: string;
  path: string;
  icon: string;
  exact?: boolean;
}

interface NavigationGroup {
  label: string;
  icon: string;
  items: NavigationItem[];
}

/** Larguras de referência do layout; ver app-shell.component.scss. */
const EXPANDED = '(min-width: 1280px)';
const MOBILE = '(max-width: 767.98px)';
/** A partir daqui o painel do assistente empurra o conteúdo em vez de cobri-lo. */
const AGENT_PUSH = '(min-width: 1024px)';

@Component({
  selector: 'app-shell',
  imports: [
    AgentPanelComponent,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatSidenavModule,
    MatTooltipModule,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
  ],
  template: `
    <a class="skip-link" href="#main-content">Ir para o conteúdo</a>

    <mat-sidenav-container class="shell" [class.rail]="rail && !isMobile">
      <mat-sidenav
        #drawer
        class="app-sidebar"
        [mode]="isMobile ? 'over' : 'side'"
        [opened]="isMobile ? drawerOpen : true"
        (openedChange)="drawerOpen = $event"
        aria-label="Navegação principal"
      >
        <div class="brand">
          @if (!rail) {
            <a class="brand-link" routerLink="/" (click)="closeOnMobile()">
              <span class="brand-mark" aria-hidden="true">N</span>
              <span class="brand-name">
                NotaFlow
                <small>Smart Billing</small>
              </span>
            </a>
          }
          <!--
            Em modo trilha o botão é a ÚNICA forma de reabrir o menu, então ele
            ocupa a linha da marca em vez de sumir.
          -->
          <button
            type="button"
            class="collapse"
            [matTooltip]="rail ? 'Expandir menu' : ''"
            matTooltipPosition="right"
            [attr.aria-label]="rail ? 'Expandir menu' : 'Recolher menu'"
            [attr.aria-expanded]="!rail"
            (click)="toggleRail()"
          >
            <mat-icon [svgIcon]="rail ? 'panel-left-open' : 'panel-left-close'" />
          </button>
        </div>

        <!-- Busca de NAVEGAÇÃO: filtra módulos e funções, não dados. -->
        <div class="nav-search">
          <mat-icon svgIcon="search" aria-hidden="true" />
          <input
            type="search"
            name="navQuery"
            [(ngModel)]="navQuery"
            placeholder="Buscar no menu"
            aria-label="Buscar módulos e funções"
          />
        </div>

        <nav class="nav" aria-label="Seções do sistema">
          @if (matches('Visão geral')) {
            <a
              class="nav-item"
              routerLink="/"
              routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: true }"
              [matTooltip]="rail ? 'Visão geral' : ''"
              matTooltipPosition="right"
              (click)="closeOnMobile()"
            >
              <mat-icon svgIcon="layout-dashboard" aria-hidden="true" />
              <span class="nav-text">Visão geral</span>
            </a>
          }

          @if (matches('Notas abertas')) {
            <p class="nav-label">Favorito</p>
            <!--
              Favorito é uma CONSULTA salva, não um atalho para a mesma tela do
              módulo: repetir o item faria dois links idênticos ficarem ativos
              ao mesmo tempo.
            -->
            <a
              class="nav-item"
              routerLink="/notas"
              [queryParams]="{ situacao: 'abertas' }"
              routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: true, queryParams: 'exact', matrixParams: 'ignored', fragment: 'ignored', paths: 'exact' }"
              [matTooltip]="rail ? 'Notas abertas' : ''"
              matTooltipPosition="right"
              (click)="closeOnMobile()"
            >
              <mat-icon svgIcon="star" aria-hidden="true" />
              <span class="nav-text">Notas abertas</span>
            </a>
          }

          @if (visibleGroups().length) {
            <p class="nav-label">Módulos</p>
          }
          @for (group of visibleGroups(); track group.label) {
            <p class="nav-group">
              <mat-icon [svgIcon]="group.icon" aria-hidden="true" />
              <span class="nav-text">{{ group.label }}</span>
            </p>
            @for (item of group.items; track item.path) {
              <a
                class="nav-item nav-child"
                [routerLink]="item.path"
                routerLinkActive="active"
                [routerLinkActiveOptions]="{ exact: item.exact ?? false }"
                [matTooltip]="rail ? item.label : ''"
                matTooltipPosition="right"
                (click)="closeOnMobile()"
              >
                <mat-icon [svgIcon]="item.icon" aria-hidden="true" />
                <span class="nav-text">{{ item.label }}</span>
              </a>
            }
          }

          @if (!hasResults()) {
            <p class="nav-empty">Nada encontrado para “{{ navQuery }}”.</p>
          }
        </nav>

        <div class="sidebar-footer">
          <span class="env">DEMONSTRAÇÃO</span>
          <span class="version">v1.0</span>
        </div>
      </mat-sidenav>

      <mat-sidenav-content class="app-main">
        <!-- Faixa GLOBAL: idêntica em qualquer módulo. -->
        <div class="bar-global">
          @if (isMobile) {
            <button
              mat-icon-button
              type="button"
              aria-label="Abrir menu principal"
              (click)="drawer.toggle()"
            >
              <mat-icon svgIcon="panel-left-open" />
            </button>
          }
          <div class="bar-global-right">
            <button
              type="button"
              class="assistant"
              [class.on]="agentOpen"
              [attr.aria-expanded]="agentOpen"
              (click)="toggleAgent()"
            >
              <mat-icon svgIcon="sparkles" aria-hidden="true" />
              <span>Assistente</span>
            </button>

            <span class="gsep"></span>

            <span class="user">
              <span class="avatar" aria-hidden="true">{{ initials() }}</span>
              <span class="user-name">{{ auth.user()?.displayName }}</span>
            </span>
            <button type="button" class="icon-btn" aria-label="Sair" (click)="signOut()">
              <mat-icon svgIcon="log-out" />
            </button>
          </div>
        </div>

        <div class="work-area">
          <main id="main-content" class="content" tabindex="-1">
            <router-outlet />
          </main>

          @if (agentOpen) {
            @if (agentOverlay) {
              <button
                type="button"
                class="agent-scrim"
                aria-label="Fechar assistente"
                (click)="agentOpen = false"
              ></button>
            }
            <app-agent-panel
              class="agent-slot"
              [class.overlay]="agentOverlay"
              [class.fullscreen]="isMobile"
              (closed)="agentOpen = false"
            />
          }
        </div>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styleUrl: './app-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.Default,
})
export class AppShellComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Só entram itens cuja rota existe. Importar NF e Movimentações aparecem
   * quando as telas forem implementadas — menu que não abre nada é pior do
   * que menu curto.
   */
  private readonly groups: NavigationGroup[] = [
    {
      label: 'Fiscal',
      icon: 'file-text',
      items: [
        { label: 'Notas fiscais', path: '/notas', icon: 'file-text' },
        { label: 'Movimentações', path: '/movimentacoes', icon: 'activity' },
      ],
    },
    {
      label: 'Estoque',
      icon: 'package',
      items: [{ label: 'Produtos', path: '/produtos', icon: 'package' }],
    },
  ];

  navQuery = '';
  isMobile = false;
  rail = false;
  drawerOpen = false;
  agentOpen = false;
  /** Abaixo de 1024px o painel cobre o conteúdo: empurrar deixaria o grid inutilizável. */
  agentOverlay = false;

  constructor() {
    this.breakpointObserver
      .observe([EXPANDED, MOBILE, AGENT_PUSH])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((state) => {
        this.isMobile = state.breakpoints[MOBILE];
        // Entre 768px e 1279px a sidebar vira trilha de ícones; o usuário
        // ainda pode alternar manualmente pelo botão.
        this.rail = !this.isMobile && !state.breakpoints[EXPANDED];
        this.agentOverlay = !state.breakpoints[AGENT_PUSH];
        if (!this.isMobile) this.drawerOpen = false;
      });
  }

  initials(): string {
    const name = this.auth.user()?.displayName ?? '';
    return name
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toLocaleUpperCase('pt-BR') ?? '')
      .join('');
  }

  signOut(): void {
    this.auth.signOut();
    void this.router.navigate(['/entrar']);
  }

  toggleAgent(): void {
    this.agentOpen = !this.agentOpen;
  }

  toggleRail(): void {
    this.rail = !this.rail;
  }

  closeOnMobile(): void {
    if (this.isMobile) this.drawerOpen = false;
  }

  matches(label: string): boolean {
    return this.normalize(label).includes(this.normalize(this.navQuery));
  }

  /** O aviso de "nada encontrado" considera o menu inteiro, não só os módulos. */
  hasResults(): boolean {
    return (
      this.matches('Visão geral') ||
      this.matches('Notas abertas') ||
      this.visibleGroups().length > 0
    );
  }

  visibleGroups(): NavigationGroup[] {
    const query = this.normalize(this.navQuery);
    if (!query) return this.groups;

    return this.groups
      .map((group) => {
        // Um módulo que casa pelo próprio nome mantém todas as suas telas.
        if (this.normalize(group.label).includes(query)) return group;
        const items = group.items.filter((item) =>
          this.normalize(item.label).includes(query),
        );
        return { ...group, items };
      })
      .filter((group) => group.items.length > 0);
  }

  private normalize(value: string): string {
    return value
      .trim()
      .toLocaleLowerCase('pt-BR')
      .normalize('NFD')
      .replace(/\p{Diacritic}/gu, '');
  }
}
