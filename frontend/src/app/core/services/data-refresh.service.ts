import { Injectable } from '@angular/core';
import { Subject, filter, map } from 'rxjs';
import type { Observable } from 'rxjs';

/** Áreas que uma ação pode invalidar. */
export type DataArea = 'produtos' | 'notas' | 'movimentacoes';

/**
 * Aviso de que uma área mudou por fora da tela que a exibe.
 *
 * Sem isto, cadastrar produto pelo assistente e cair na lista mostrava a lista
 * velha: a página já estava montada, então navegar até ela não recarrega nada.
 * Quem escreve avisa aqui; quem exibe escuta e recarrega.
 *
 * É um fluxo, e não um sinal com contador, porque sinal tem valor corrente — e
 * um `effect` lendo esse valor dispararia já na montagem, fazendo cada tela
 * carregar duas vezes ao abrir. Aqui só há emissão quando algo de fato mudou.
 */
@Injectable({ providedIn: 'root' })
export class DataRefreshService {
  private readonly changed = new Subject<DataArea>();

  /**
   * Marca as áreas afetadas. Criar nota move estoque, então quem fecha avisa
   * também movimentações — é mais barato recarregar à toa do que exibir saldo
   * desatualizado.
   */
  invalidate(...areas: DataArea[]): void {
    for (const area of areas) this.changed.next(area);
  }

  /** Emite quando a área indicada mudou. */
  on(area: DataArea): Observable<void> {
    return this.changed.pipe(
      filter((changed) => changed === area),
      map(() => undefined),
    );
  }
}
