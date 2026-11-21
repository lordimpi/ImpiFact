import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Injector, runInInjectionContext, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { municipalityPicker } from './municipality-picker';

const MUNICIPALITIES = '/api/v1/catalogs/municipalities';
const DEPARTMENTS = '/api/v1/catalogs/departments';

const row = (id: number, name: string, departmentId: number) => ({
  id,
  code: String(id),
  name,
  is_active: true,
  department_id: departmentId,
});

describe('municipalityPicker', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  const settle = async () => {
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);
    TestBed.tick();
  };
  const municipalityRequests = () => http.match((request) => request.url === MUNICIPALITIES);

  function create(current = signal('')) {
    const cleared: string[] = [];
    const picker = runInInjectionContext(TestBed.inject(Injector), () =>
      municipalityPicker({
        current,
        otherName: (id) => `Otro (id ${id})`,
        clear: () => {
          cleared.push(current());
          current.set('');
        },
      }),
    );
    return { picker, current, cleared };
  }

  function flushDepartments() {
    http.expectOne(DEPARTMENTS).flush({
      name: 'departments',
      version: 'd1',
      next_cursor: null,
      items: [
        { id: 5, code: '11', name: 'Bogotá', is_active: true, country_id: 46 },
        { id: 11, code: '19', name: 'Cauca', is_active: true, country_id: 46 },
      ],
    });
  }

  it('lists the departments and asks for municipalities only after one is chosen', async () => {
    const { picker } = create();
    flushDepartments();
    await settle();
    expect(picker.departments().map((option) => option.name)).toEqual(['Bogotá', 'Cauca']);
    expect(municipalityRequests()).toEqual([]);
    expect(picker.municipalities.state().status).toBe('idle');
    // S9: the municipality stays locked until a department is chosen.
    expect(picker.municipalityLocked()).toBe(true);

    picker.selectDepartment('11');
    await settle();
    expect(picker.municipalityLocked()).toBe(false);
    const [cauca] = municipalityRequests();
    expect(cauca!.request.params.get('department_id')).toBe('11');
    cauca!.flush({
      name: 'municipalities',
      version: 'm1',
      next_cursor: null,
      items: [row(362, 'Popayán', 11)],
    });
    await settle();
    expect(picker.municipalities.options()).toEqual([{ id: 362, name: 'Popayán' }]);
  });

  it('clears the municipality when another department is chosen', async () => {
    const { picker, current, cleared } = create();
    flushDepartments();
    picker.selectDepartment('11');
    await settle();
    municipalityRequests()[0]!.flush({
      name: 'municipalities',
      version: 'm1',
      next_cursor: null,
      items: [],
    });
    current.set('362');
    await settle();

    picker.selectDepartment('11');
    expect(cleared).toEqual([]);
    picker.selectDepartment('5');
    await settle();
    expect(cleared).toEqual(['362']);
    expect(picker.department()).toBe('5');
    municipalityRequests().forEach((request) =>
      request.flush({ name: 'municipalities', version: 'm1', next_cursor: null, items: [] }),
    );
  });

  it('preselects the department of a saved municipality', async () => {
    const { picker } = create(signal('362'));
    flushDepartments();
    await settle();
    // Resuelve el municipio guardado recorriendo el catálogo sin filtro.
    const [lookup] = municipalityRequests();
    expect(lookup!.request.params.has('department_id')).toBe(false);
    lookup!.flush({
      name: 'municipalities',
      version: 'm1',
      next_cursor: null,
      items: [row(362, 'Popayán', 11)],
    });
    await settle();
    expect(picker.department()).toBe('11');
    const [cauca] = municipalityRequests();
    expect(cauca!.request.params.get('department_id')).toBe('11');
    cauca!.flush({
      name: 'municipalities',
      version: 'm1',
      next_cursor: null,
      items: [row(362, 'Popayán', 11)],
    });
    await settle();
    expect(picker.municipalities.options()).toEqual([{ id: 362, name: 'Popayán' }]);
  });
});
