import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ProductService } from './product.service';

describe('ProductService', () => {
  let service: ProductService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ProductService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('normaliza respostas paginadas do catálogo', () => {
    service.list('mouse', 1, 20).subscribe((page) => {
      expect(page.total).toBe(1);
      expect(page.items[0].code).toBe('MOU-001');
    });

    const request = http.expectOne(
      (candidate) =>
        candidate.url === `${environment.inventoryApiUrl}/products` &&
        candidate.params.get('query') === 'mouse',
    );
    request.flush({
      items: [
        {
          id: 'product-1',
          code: 'MOU-001',
          description: 'Mouse sem fio',
          balance: 8,
          tracksStock: true,
          createdAt: '2026-08-13T12:00:00Z',
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
  });

  it('envia somente os campos permitidos ao cadastrar', () => {
    const payload = { code: 'TEC-001', description: 'Teclado', balance: 5, tracksStock: true };
    service.create(payload).subscribe();

    const request = http.expectOne(`${environment.inventoryApiUrl}/products`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(payload);
    request.flush({ id: 'product-2', ...payload, createdAt: '2026-08-13T12:00:00Z' });
  });
});
