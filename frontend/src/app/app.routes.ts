import type { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { AppShellComponent } from './layout/app-shell.component';

export const routes: Routes = [
  {
    // Fora do shell: quem não tem sessão não vê menu nem assistente.
    path: 'entrar',
    loadComponent: () =>
      import('./features/auth/login.page').then((component) => component.LoginPage),
    title: 'Entrar | NotaFlow',
  },
  {
    // O shell é o layout das rotas de negócio, todas atrás da guarda.
    path: '',
    component: AppShellComponent,
    canActivate: [authGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
          import('./features/dashboard/dashboard.page').then(
            (component) => component.DashboardPage,
          ),
        title: 'Visão geral | NotaFlow',
      },
      {
        path: 'produtos',
        loadComponent: () =>
          import('./features/products/products.page').then(
            (component) => component.ProductsPage,
          ),
        title: 'Produtos | NotaFlow',
      },
      {
        path: 'notas',
        loadComponent: () =>
          import('./features/invoices/invoices.page').then(
            (component) => component.InvoicesPage,
          ),
        title: 'Notas fiscais | NotaFlow',
      },
      {
        path: 'movimentacoes',
        loadComponent: () =>
          import('./features/movements/movements.page').then(
            (component) => component.MovementsPage,
          ),
        title: 'Movimentações | NotaFlow',
      },
      {
        // Link direto para uma nota: renderiza a listagem, que abre o detalhe
        // em modal sobre ela. Mantém a URL compartilhável sem trocar de tela.
        path: 'notas/:id',
        loadComponent: () =>
          import('./features/invoices/invoices.page').then(
            (component) => component.InvoicesPage,
          ),
        title: 'Detalhes da nota | NotaFlow',
      },
      {
        // O assistente virou painel lateral global; a rota antiga redireciona
        // para não quebrar links guardados.
        path: 'copiloto',
        redirectTo: '',
        pathMatch: 'full',
      },
    ],
  },
  {
    path: '**',
    loadComponent: () =>
      import('./features/not-found/not-found.page').then(
        (component) => component.NotFoundPage,
      ),
    title: 'Página não encontrada | NotaFlow',
  },
];
