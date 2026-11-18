import { Signal, computed, effect, inject, signal, untracked } from '@angular/core';
import { CatalogSearch, catalogSearch } from './catalog-search';
import { CatalogOption, CatalogsStore, catalogOptions } from './catalogs.store';

export interface MunicipalityPickerOptions {
  /** Municipio guardado en el formulario (su id como texto). */
  current: () => string;
  /** Nombre de un id que todavía no se conoce («Otro (id N)»). */
  otherName: (id: number) => string;
  /** Vacía el municipio del formulario: se llama cuando la persona elige otro departamento. */
  clear: () => void;
}

export interface MunicipalityPicker {
  /** Departamento elegido (su id como texto); vacío mientras no hay ninguno. */
  department: Signal<string>;
  /** Opciones de la lista de departamentos (catálogo `departments`, completo). */
  departments: Signal<readonly CatalogOption[]>;
  /** Departamentos que coinciden con lo escrito en su combobox (sin distinguir mayúsculas ni tildes). */
  departmentOptions: Signal<readonly CatalogOption[]>;
  /** La persona escribe en el combobox de departamentos. */
  searchDepartments(term: string): void;
  /** Búsqueda de los municipios del departamento elegido, para `ui-combobox`. */
  municipalities: CatalogSearch;
  /** La persona eligió un departamento: si cambió, el municipio anterior se vacía. */
  selectDepartment(id: string): void;
}

/**
 * Departamento y municipio en dos pasos: primero el departamento y luego un combobox con sus municipios, que se
 * buscan en el servidor (`department_id` y `q`). El Api solo guarda el municipio; al editar un registro, el
 * departamento se toma del municipio guardado. Debe llamarse en un contexto de inyección.
 */
export function municipalityPicker(options: MunicipalityPickerOptions): MunicipalityPicker {
  const store = inject(CatalogsStore);
  const department = signal('');
  const departmentItems = store.items('departments');
  const departments = computed(() => catalogOptions('departments', departmentItems()));
  // Son 33: se filtran en el navegador, sin pedirle nada al servidor.
  const departmentTerm = signal('');
  const departmentOptions = computed(() => {
    const term = normalize(departmentTerm());
    return term === ''
      ? departments()
      : departments().filter((option) => normalize(option.name).includes(term));
  });

  const municipalities = catalogSearch('municipalities', {
    current: options.current,
    otherName: options.otherName,
    format: (item) => item.name,
    filter: () => (department() === '' ? null : { departmentId: Number(department()) }),
  });

  // Con un municipio guardado y sin departamento elegido, el departamento sale del municipio (una vez por id).
  let resolvedFor: string | null = null;
  effect(() => {
    const current = options.current();
    const id = Number(current);
    if (current === '' || !Number.isInteger(id) || department() !== '' || resolvedFor === current) {
      return;
    }
    resolvedFor = current;
    void store
      .find('municipalities', id)
      .then((item) => {
        if (
          item?.departmentId &&
          untracked(department) === '' &&
          untracked(options.current) === current
        ) {
          department.set(String(item.departmentId));
        }
      })
      .catch(() => undefined);
  });

  return {
    department: department.asReadonly(),
    departments,
    departmentOptions,
    searchDepartments: (term) => departmentTerm.set(term),
    municipalities,
    selectDepartment: (id) => {
      if (id === untracked(department)) {
        return;
      }
      department.set(id);
      if (untracked(options.current) !== '') {
        options.clear();
      }
    },
  };
}

/** Texto para comparar sin distinguir mayúsculas ni tildes («Bogotá» = «bogota»). */
function normalize(text: string): string {
  return text
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .trim();
}
