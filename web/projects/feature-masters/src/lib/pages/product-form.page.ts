import { HttpContext } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { FormField, submit } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { Api, ProductResponse, createProduct, getProduct, updateProduct } from '@impifact/api';
import {
  AppError,
  CatalogsStore,
  HasUnsavedChanges,
  SKIP_ERROR_TOAST,
  ToastStore,
  catalogOptions,
  catalogSearch,
  optionsWith,
} from '@impifact/core';
import {
  ButtonComponent,
  CardComponent,
  CardFooterDirective,
  ComboboxComponent,
  ComboboxLabels,
  ErrorStateComponent,
  FieldComponent,
  InputDirective,
  LoadingComponent,
  PageTitleComponent,
  SelectComponent,
} from '@impifact/ui';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { fieldMessage, serverFieldErrors } from '../forms/form-messages';
import {
  PRODUCT_FIELDS,
  ProductModel,
  createProductForm,
  emptyProduct,
  hasPerUnit,
  hasPercent,
  productCommand,
  productModel,
} from '../forms/product.form';
import { mastersText, provideMastersI18n } from '../masters-i18n';
import { WITHHOLDING_TAXES } from '../models/catalogs';

/** Errores de guardar y de cargar se muestran en la página, no en un aviso flotante. */
const IN_FORM = () => new HttpContext().set(SKIP_ERROR_TOAST, true);

/** Campos con importes: su error de formato explica cómo escribir el número. */
const AMOUNT_FIELDS: readonly (keyof ProductModel)[] = [
  'unit_price',
  'tax_percent',
  'tax_per_unit_amount',
];

/**
 * Alta (`/productos/nuevo`) y edición (`/productos/:id`) de un producto o servicio: código, nombre, unidad, precio
 * sin impuestos y, si lleva, el impuesto con su tarifa o su valor por unidad. En la edición se puede desactivar o
 * reactivar. Los errores por campo del Api se muestran junto a cada campo.
 */
@Component({
  selector: 'feat-product-form-page',
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    ButtonComponent,
    CardComponent,
    CardFooterDirective,
    ComboboxComponent,
    ErrorStateComponent,
    FieldComponent,
    InputDirective,
    LoadingComponent,
    PageTitleComponent,
    SelectComponent,
  ],
  providers: [provideMastersI18n()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-form.page.html',
  styleUrl: './masters-page.css',
})
export class ProductFormPage implements HasUnsavedChanges {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastStore);
  private readonly transloco = inject(TranslocoService);

  /** Id del producto en la ruta; sin él, es un alta. */
  readonly id = input<string>();

  private readonly model = signal<ProductModel>(emptyProduct());
  protected readonly productForm = createProductForm(this.model);

  /** Producto cargado (edición): conserva el tipo de identificación del artículo al guardar. */
  private readonly loaded = signal<ProductResponse | null>(null);
  protected readonly loadState = signal<'loading' | 'ready' | 'error'>('ready');
  protected readonly generalError = signal<string | null>(null);
  protected readonly submitting = computed(() => this.productForm().submitting());
  private saved = false;

  protected readonly isEdit = computed(() => !!this.id());
  protected readonly showPercent = computed(() => hasPercent(this.model().tax_id));
  protected readonly showPerUnit = computed(() => hasPerUnit(this.model().tax_id));

  // Catálogos de la DIAN en el Api: la unidad se busca en el servidor (son 1.093) y los impuestos llegan enteros.
  private readonly catalogs = inject(CatalogsStore);
  private readonly other = (id: number) => this.text('common.otherOption', { id });
  protected readonly unitMeasures = catalogSearch('unit-measures', {
    current: () => this.model().unit_measure_id,
    otherName: this.other,
  });
  protected readonly unitMeasureLabels: ComboboxLabels = {
    loading: this.text('common.catalog.loading'),
    error: this.text('common.catalog.error'),
    empty: this.text('products.form.unitMeasureNone'),
    retry: this.text('common.catalog.retry'),
  };
  private readonly taxItems = this.catalogs.items('taxes');
  /** Taxes of the select; «Sin impuesto» ('') first, so the tax can be taken off again. */
  protected readonly taxes = computed(() => [
    { id: '', name: this.text('products.form.noTax') },
    ...optionsWith(
      catalogOptions(
        'taxes',
        this.taxItems().filter((item) => !WITHHOLDING_TAXES.includes(item.id)),
      ),
      this.model().tax_id,
      this.other,
    ),
  ]);

  /** Mensaje bajo cada campo, por nombre del campo. */
  protected readonly errors = computed(() => {
    const translate = (key: string, params?: Record<string, unknown>) => this.text(key, params);
    return Object.fromEntries(
      PRODUCT_FIELDS.map((name) => [
        name,
        fieldMessage(
          this.productForm[name](),
          translate,
          AMOUNT_FIELDS.includes(name) ? 'products.form.numberFormat' : undefined,
        ),
      ]),
    ) as Record<keyof ProductModel, string | null>;
  });

  constructor() {
    effect(() => {
      const id = this.id();
      if (id) {
        untracked(() => void this.load(id));
      }
    });
  }

  hasUnsavedChanges(): boolean {
    return !this.saved && this.productForm().dirty();
  }

  protected async load(id = this.id()): Promise<void> {
    if (!id) {
      return;
    }
    this.loadState.set('loading');
    try {
      const product = await firstValueFrom(this.api.invoke(getProduct, { id }, IN_FORM()));
      this.loaded.set(product);
      this.model.set(productModel(product));
      this.loadState.set('ready');
    } catch {
      this.loadState.set('error');
    }
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.generalError.set(null);
    await submit(this.productForm, {
      action: async () => {
        try {
          await this.save();
        } catch (error) {
          return this.showError(error);
        }
        this.saved = true;
        this.toasts.show({ kind: 'success', text: this.text('products.form.saved') });
        await this.router.navigateByUrl('/productos');
        return undefined;
      },
      onInvalid: () => this.focusFirstInvalid(),
    });
  }

  private save(): Promise<unknown> {
    const model = this.model();
    const body = productCommand(model);
    const id = this.id();
    if (!id) {
      return firstValueFrom(this.api.invoke(createProduct, { body }, IN_FORM()));
    }
    return firstValueFrom(
      this.api.invoke(
        updateProduct,
        {
          id,
          body: {
            ...body,
            type_item_identification_id: this.loaded()?.type_item_identification_id ?? null,
            is_active: model.is_active,
          },
        },
        IN_FORM(),
      ),
    );
  }

  /** Errores por campo junto a su campo; el resto en el aviso general del formulario. */
  private showError(error: unknown) {
    if (!(error instanceof AppError)) {
      this.generalError.set(this.text('common.errors.unexpected'));
      return undefined;
    }
    const onFields = serverFieldErrors(this.productForm, PRODUCT_FIELDS, error);
    if (onFields.length === 0) {
      this.generalError.set(
        error.code === 'product.duplicate' ? this.text('products.form.duplicate') : error.message,
      );
    }
    return onFields;
  }

  private focusFirstInvalid(): void {
    const first = PRODUCT_FIELDS.map((name) => this.productForm[name]).find((field) =>
      field().invalid(),
    );
    first?.().focusBoundControl();
  }

  protected text(key: string, params?: Record<string, unknown>): string {
    return mastersText(this.transloco, key, params);
  }
}
