import { HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import type { ProblemDetails, UiError } from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class ApiErrorService {
  from(error: unknown): UiError {
    if (!(error instanceof HttpErrorResponse)) {
      return {
        title: 'Algo não saiu como esperado',
        message: 'Tente novamente. Se o problema continuar, recarregue a página.',
        status: 0,
      };
    }

    const problem = this.problemDetails(error.error);
    const status = error.status;
    const code = problem?.code;

    const knownMessages: Record<string, string> = {
      INSUFFICIENT_STOCK:
        'Não há saldo suficiente para todos os itens. Nenhum produto foi descontado.',
      INVENTORY_UNAVAILABLE:
        'O serviço de estoque está temporariamente indisponível. A nota continua segura para nova tentativa.',
      INVOICE_ALREADY_CLOSED: 'Esta nota já foi fechada e não pode ser alterada.',
      INVOICE_NOT_CLOSED: 'O PDF só fica disponível depois que a nota é fechada.',
      IDEMPOTENCY_KEY_REUSED:
        'A operação foi recusada porque os dados não correspondem à tentativa original.',
      AI_UNAVAILABLE:
        'O Copiloto está indisponível agora. Você pode continuar pelo preenchimento manual.',
      AI_BUSY:
        'O Copiloto já está processando outra solicitação. Aguarde alguns segundos e tente novamente.',
      AI_DISABLED:
        'O Copiloto está desabilitado porque a chave da OpenAI não foi configurada. O preenchimento manual continua disponível.',
      PRODUCT_CODE_ALREADY_EXISTS:
        'Já existe um produto com esse código. Escolha um código diferente.',
      PRODUCT_NOT_FOUND: 'O produto solicitado não foi encontrado.',
      INVOICE_NOT_FOUND: 'A nota solicitada não foi encontrada.',
      VALIDATION_ERROR: 'Revise os campos informados e tente novamente.',
    };

    const defaultMessage =
      status === 0
        ? 'Não foi possível conectar aos serviços. Confira se o ambiente está em execução.'
        : status >= 500
          ? 'O serviço encontrou uma instabilidade. Tente novamente em instantes.'
          : 'Não foi possível concluir a solicitação.';

    return {
      title: problem?.title ?? this.titleForStatus(status),
      message:
        (code ? knownMessages[code] : undefined) ??
        problem?.detail ??
        this.firstValidationError(problem) ??
        defaultMessage,
      status,
      code,
      traceId: problem?.traceId,
    };
  }

  private problemDetails(value: unknown): ProblemDetails | null {
    if (typeof value !== 'object' || value === null) {
      return null;
    }
    return value as ProblemDetails;
  }

  private firstValidationError(problem: ProblemDetails | null): string | undefined {
    if (!problem?.errors) {
      return undefined;
    }
    return Object.values(problem.errors).flat()[0];
  }

  private titleForStatus(status: number): string {
    if (status === 404) return 'Não encontrado';
    if (status === 409) return 'Não foi possível concluir';
    if (status === 503 || status === 0) return 'Serviço indisponível';
    if (status >= 500) return 'Instabilidade temporária';
    return 'Revise a solicitação';
  }
}
