/** Extrato de movimentação de estoque (UC-09). */
export interface StockMovement {
  id: string;
  productId: string;
  code: string;
  description: string;
  quantity: number;
  balanceBefore: number;
  balanceAfter: number;
  invoiceId: string;
  createdAt: string;
}
