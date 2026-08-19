export interface Product {
  id: string;
  code: string;
  description: string;
  balance: number;
  /** Falso para serviços e itens sob encomenda: não valida nem movimenta saldo. */
  tracksStock: boolean;
  createdAt: string;
}

export interface CreateProductRequest {
  code: string;
  description: string;
  balance: number;
  tracksStock: boolean;
}
