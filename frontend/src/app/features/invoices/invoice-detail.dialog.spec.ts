import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import type { ComponentFixture } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { of, throwError } from 'rxjs';
import type { Invoice } from '../../core/models/invoice.model';
import { ApiErrorService } from '../../core/services/api-error.service';
import { ExportService } from '../../core/services/export.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { NotificationService } from '../../core/services/notification.service';
import { InvoiceDetailDialog } from './invoice-detail.dialog';

describe('InvoiceDetailDialog', () => {
  let fixture: ComponentFixture<InvoiceDetailDialog>;
  let component: InvoiceDetailDialog;
  let dialogRef: { close: jasmine.Spy; disableClose: boolean };
  let invoiceService: jasmine.SpyObj<InvoiceService>;

  const openInvoice: Invoice = {
    id: 'invoice-1',
    number: 1,
    status: 'Open',
    createdAt: '2026-08-20T12:00:00Z',
    createdBy: 'Operador',
    items: [],
    closure: null,
  };

  const closedInvoice: Invoice = {
    ...openInvoice,
    status: 'Closed',
    closedAt: '2026-08-20T12:01:00Z',
  };

  beforeEach(async () => {
    dialogRef = { close: jasmine.createSpy('close'), disableClose: false };
    invoiceService = jasmine.createSpyObj<InvoiceService>('InvoiceService', [
      'close',
      'downloadPdf',
      'getById',
    ]);

    await TestBed.configureTestingModule({
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { id: openInvoice.id } },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: InvoiceService, useValue: invoiceService },
        { provide: ApiErrorService, useValue: { from: () => ({ title: 'Erro', message: 'Falhou' }) } },
        { provide: NotificationService, useValue: jasmine.createSpyObj('NotificationService', ['success', 'info', 'warning', 'error']) },
        { provide: ExportService, useValue: jasmine.createSpyObj('ExportService', ['toCsv']) },
      ],
    })
      // O comportamento sob teste é da classe; não dependemos dos ícones e do
      // layout Material para cobrir a transição assíncrona.
      .overrideComponent(InvoiceDetailDialog, { set: { template: '' } })
      .compileComponents();

    fixture = TestBed.createComponent(InvoiceDetailDialog);
    component = fixture.componentInstance;
  });

  it('fecha o modal somente depois de iniciar o PDF após um fechamento imediato', () => {
    component.invoice = openInvoice;
    invoiceService.close.and.returnValue(of({ httpStatus: 200, state: 'Completed', invoice: closedInvoice }));
    invoiceService.downloadPdf.and.returnValue(of(new Blob(['pdf'], { type: 'application/pdf' })));
    spyOn(URL, 'createObjectURL').and.returnValue('blob:nota');
    spyOn(URL, 'revokeObjectURL');
    spyOn(HTMLAnchorElement.prototype, 'click').and.stub();

    component.requestClose();

    expect(invoiceService.downloadPdf).toHaveBeenCalledWith(openInvoice.id);
    expect(dialogRef.close).toHaveBeenCalledTimes(1);
  });

  it('mantém o modal aberto quando o PDF falha, para permitir segunda tentativa', () => {
    component.invoice = openInvoice;
    invoiceService.close.and.returnValue(of({ httpStatus: 200, state: 'Completed', invoice: closedInvoice }));
    invoiceService.downloadPdf.and.returnValue(throwError(() => new Error('PDF indisponível')));

    component.requestClose();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(dialogRef.disableClose).toBeFalse();
  });

  it('fecha o modal depois de o polling confirmar a nota e iniciar o PDF', fakeAsync(() => {
    const pendingInvoice: Invoice = {
      ...openInvoice,
      closure: {
        attemptId: 'attempt-1',
        state: 'Pending',
        retryCount: 0,
        updatedAt: '2026-08-20T12:00:00Z',
      },
    };
    component.invoice = openInvoice;
    invoiceService.close.and.returnValue(of({ httpStatus: 202, state: 'Pending', invoice: pendingInvoice }));
    invoiceService.getById.and.returnValue(of(closedInvoice));
    invoiceService.downloadPdf.and.returnValue(of(new Blob(['pdf'], { type: 'application/pdf' })));
    spyOn(URL, 'createObjectURL').and.returnValue('blob:nota');
    spyOn(URL, 'revokeObjectURL');
    spyOn(HTMLAnchorElement.prototype, 'click').and.stub();

    component.requestClose();
    tick(0);

    expect(dialogRef.close).toHaveBeenCalledTimes(1);
  }));
});
