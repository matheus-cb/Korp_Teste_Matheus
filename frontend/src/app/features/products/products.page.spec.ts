import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import type { ValueFormatterParams } from 'ag-grid-community';
import { NEVER, of } from 'rxjs';
import { ApiErrorService } from '../../core/services/api-error.service';
import { DataRefreshService } from '../../core/services/data-refresh.service';
import { ExportService } from '../../core/services/export.service';
import { ProductService } from '../../core/services/product.service';
import { ProductsPage } from './products.page';

registerLocaleData(localePt, 'pt-BR');

describe('ProductsPage', () => {
  let component: ProductsPage;

  beforeEach(async () => {
    const productService = jasmine.createSpyObj<ProductService>('ProductService', ['list']);
    productService.list.and.returnValue(of({ items: [], total: 0, page: 1, pageSize: 100 }));

    await TestBed.configureTestingModule({
      providers: [
        { provide: ProductService, useValue: productService },
        { provide: DataRefreshService, useValue: { on: () => NEVER } },
        { provide: ApiErrorService, useValue: { from: () => ({ title: 'Erro', message: 'Falhou' }) } },
        { provide: ExportService, useValue: jasmine.createSpyObj('ExportService', ['toCsv']) },
        { provide: MatDialog, useValue: jasmine.createSpyObj('MatDialog', ['open']) },
      ],
    })
      // A regra testada é a definição das colunas; ícones e grid são validados
      // pelo build de produção, sem acoplar este teste ao layout Material.
      .overrideComponent(ProductsPage, { set: { template: '' } })
      .compileComponents();

    component = TestBed.createComponent(ProductsPage).componentInstance;
  });

  it('separa autoria e data de criação e alteração em colunas próprias', () => {
    const createdBy = component.columns.find((column) => column.field === 'createdBy');
    const createdAt = component.columns.find((column) => column.field === 'createdAt');
    const updatedBy = component.columns.find((column) => column.field === 'updatedBy');
    const updatedAt = component.columns.find((column) => column.field === 'updatedAt');

    expect(createdBy?.headerName).toBe('Criado por');
    expect(createdAt?.headerName).toBe('Data de criação');
    expect(updatedBy?.headerName).toBe('Alterado por');
    expect(updatedAt?.headerName).toBe('Data de alteração');

    const actorFormatter = createdBy?.valueFormatter as (params: ValueFormatterParams) => string;
    const dateFormatter = createdAt?.valueFormatter as (params: ValueFormatterParams) => string;
    expect(actorFormatter({ value: 'Ana', node: {} } as ValueFormatterParams)).toBe('Ana');
    expect(dateFormatter({ value: '2026-08-21T10:30:00-03:00', node: {} } as ValueFormatterParams))
      .toContain('21/08/2026');
  });
});
