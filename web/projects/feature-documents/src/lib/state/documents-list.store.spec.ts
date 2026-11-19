import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { convertToParamMap } from '@angular/router';
import { DocumentsListStore } from './documents-list.store';
import { filtersFromParams, filtersToQueryParams } from './list-filters';

describe('list filters in the URL', () => {
  it('reads every filter from the query params', () => {
    const filters = filtersFromParams(
      convertToParamMap({
        desde: '2026-11-01',
        hasta: '2026-11-30',
        tipo: 'invoice',
        estado: 'Accepted,Rejected',
        cliente: 'golden',
        numero: 'SETP99',
        total: '1',
      }),
    );

    expect(filters).toEqual({
      from: '2026-11-01',
      to: '2026-11-30',
      kind: 'invoice',
      status: ['Accepted', 'Rejected'],
      customer: 'golden',
      number: 'SETP99',
      withTotal: true,
    });
  });

  it('ignores unknown kinds and statuses and empty values', () => {
    expect(
      filtersFromParams(
        convertToParamMap({ tipo: 'receipt', estado: 'Accepted,Bogus,', cliente: ' ' }),
      ),
    ).toEqual({ status: ['Accepted'] });
  });

  it('writes the filters back, clearing the empty ones', () => {
    expect(filtersToQueryParams({ kind: 'credit_note', status: ['Sent', 'Queued'] })).toEqual({
      desde: null,
      hasta: null,
      tipo: 'credit_note',
      estado: 'Sent,Queued',
      cliente: null,
      numero: null,
      total: null,
    });
  });
});

describe('DocumentsListStore', () => {
  let store: InstanceType<typeof DocumentsListStore>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), DocumentsListStore],
    });
    store = TestBed.inject(DocumentsListStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the filters to listDocuments and asks for the total only on the first page', () => {
    store.setFilters({
      from: '2026-11-01',
      to: '2026-11-30',
      kind: 'invoice',
      status: ['Accepted', 'Rejected'],
      customer: 'golden',
      number: '990000001',
      withTotal: true,
    });

    const first = http.expectOne((request) => request.url === '/api/v1/documents');
    const params = first.request.params;
    expect(params.get('from')).toBe('2026-11-01');
    expect(params.get('to')).toBe('2026-11-30');
    expect(params.get('kind')).toBe('invoice');
    expect(params.getAll('status')).toEqual(['Accepted', 'Rejected']);
    expect(params.get('customer')).toBe('golden');
    expect(params.get('number')).toBe('990000001');
    expect(params.get('limit')).toBe('20');
    expect(params.get('include_total')).toBe('true');
    expect(params.has('cursor')).toBe(false);
    first.flush({ items: [{ id: 'd1' }], next_cursor: 'c2', total_count: 21 });

    expect(store.items()).toEqual([{ id: 'd1' }]);
    expect(store.totalCount()).toBe(21);

    store.nextPage();
    const second = http.expectOne((request) => request.url === '/api/v1/documents');
    expect(second.request.params.get('cursor')).toBe('c2');
    expect(second.request.params.has('include_total')).toBe(false);
    second.flush({ items: [{ id: 'd2' }], next_cursor: null });
    expect(store.totalCount()).toBe(21);
  });

  it('does not ask for the total unless the person wants it', () => {
    store.setFilters({});

    const request = http.expectOne((r) => r.url === '/api/v1/documents');
    expect(request.request.params.keys()).toEqual(['limit']);
    request.flush({ items: [], next_cursor: null });
    expect(store.empty()).toBe(true);
    expect(store.totalCount()).toBeNull();
  });
});
