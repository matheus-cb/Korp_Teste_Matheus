import { Injectable, computed, signal } from '@angular/core';

const STORAGE_KEY = 'notaflow.favoritos';
const MAX_FAVORITOS = 8;

/**
 * Telas que a pessoa marcou como favoritas no menu.
 *
 * Antes existia um favorito fixo no código ("Notas abertas", apontando para uma
 * consulta salva). Fixo, ele não era favorito de ninguém: aparecia para todo
 * mundo e não correspondia a nenhuma tela do módulo, então dois itens diferentes
 * levavam a lugares parecidos e nenhum dos dois era escolha do usuário.
 *
 * Agora favorito é o caminho da própria tela do módulo. Guardamos só o `path`,
 * e o rótulo e o ícone vêm da definição do menu — assim renomear um item não
 * deixa favorito órfão com nome velho.
 *
 * Persiste em `localStorage`, e não em `sessionStorage`: favorito que se perde
 * ao fechar a aba não serve para nada.
 */
@Injectable({ providedIn: 'root' })
export class FavoritesService {
  private readonly state = signal<string[]>(this.restore());

  readonly paths = this.state.asReadonly();

  readonly hasAny = computed(() => this.state().length > 0);

  isFavorite(path: string): boolean {
    return this.state().includes(path);
  }

  toggle(path: string): void {
    this.state.update((paths) =>
      paths.includes(path)
        ? paths.filter((candidate) => candidate !== path)
        : // Teto simples: o menu tem espaço limitado, e uma lista de favoritos
          // do tamanho do sistema inteiro deixa de ser atalho.
          [...paths, path].slice(-MAX_FAVORITOS),
    );
    this.persist();
  }

  private persist(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.state()));
    } catch {
      // Storage bloqueado ou cheio: os favoritos seguem só nesta sessão.
    }
  }

  private restore(): string[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return [];
      const parsed: unknown = JSON.parse(raw);
      // Leitura defensiva: conteúdo estranho é descartado em vez de derrubar o menu.
      return Array.isArray(parsed)
        ? parsed.filter((item): item is string => typeof item === 'string').slice(0, MAX_FAVORITOS)
        : [];
    } catch {
      return [];
    }
  }
}
