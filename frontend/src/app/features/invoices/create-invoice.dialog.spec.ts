import { FormBuilder } from '@angular/forms';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { of } from 'rxjs';
import { ApiErrorService } from '../../core/services/api-error.service';
import { DraftTransferService } from '../../core/services/draft-transfer.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { NotificationService } from '../../core/services/notification.service';
import { ProductService } from '../../core/services/product.service';
import { CreateInvoiceDialog } from './create-invoice.dialog';

describe('CreateInvoiceDialog', () => {
  it('preserva o código e a descrição do rascunho enquanto o catálogo carrega', async () => {
    const productService = jasmine.createSpyObj<ProductService>('ProductService', ['list']);
    productService.list.and.returnValue(of({ items: [], total: 0, page: 1, pageSize: 100 }));

    const draftTransfer = jasmine.createSpyObj<DraftTransferService>('DraftTransferService', ['take']);
    draftTransfer.take.and.returnValue([
      {
        productId: 'product-1',
        code: 'CABO-USB',
        description: 'Cabo USB-C',
        quantity: 2,
        availability: 'available',
      },
    ]);

    await TestBed.configureTestingModule({
      providers: [
        FormBuilder,
        { provide: MatDialogRef, useValue: { close: jasmine.createSpy('close') } },
        { provide: ProductService, useValue: productService },
        { provide: DraftTransferService, useValue: draftTransfer },
        { provide: InvoiceService, useValue: jasmine.createSpyObj('InvoiceService', ['create']) },
        { provide: ApiErrorService, useValue: { from: () => ({ title: 'Erro', message: 'Falhou' }) } },
        { provide: NotificationService, useValue: jasmine.createSpyObj('NotificationService', ['success']) },
      ],
    })
      .overrideComponent(CreateInvoiceDialog, { set: { template: '' } })
      .compileComponents();

    const component = TestBed.createComponent(CreateInvoiceDialog).componentInstance;
    component.ngOnInit();

    expect(component.items).toEqual([
      jasmine.objectContaining({
        productId: 'product-1',
        code: 'CABO-USB',
        description: 'Cabo USB-C',
        quantity: 2,
      }),
    ]);
  });
});
