export interface Product {
  id: string;
  code: string;
  description: string;
  balance: number;
  /** Falso para serviços e itens sob encomenda: não valida nem movimenta saldo. */
  tracksStock: boolean;
  createdAt: string;
  createdBy?: string;
  updatedAt?: string;
  updatedBy?: string;
  version?: string;
  auditEvents?: ProductAuditEvent[];
}

export interface ProductAuditEvent {
  type: 'Created' | 'Edited';
  actorName: string;
  occurredAt: string;
}

export interface CreateProductRequest {
  code: string;
  description: string;
  balance: number;
  tracksStock: boolean;
}

export interface UpdateProductRequest {
  code: string;
  description: string;
  tracksStock: boolean;
}
