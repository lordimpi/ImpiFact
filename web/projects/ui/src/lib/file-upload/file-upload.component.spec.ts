import { fireEvent, render, screen } from '@testing-library/angular';
import { FieldComponent } from '../field/field.component';
import { FileUploadComponent, formatFileSize } from './file-upload.component';

async function setUp(disabled = false) {
  const fileChange = vi.fn();
  const view = await render(
    `<ui-field label="Certificado" hint="Archivo .p12 o .pfx" error="">
       <ui-file-upload accept=".p12,.pfx" [maxFileSize]="2048" [disabled]="disabled"
         (fileChange)="fileChange($event)" />
     </ui-field>`,
    {
      imports: [FieldComponent, FileUploadComponent],
      componentProperties: { fileChange, disabled },
    },
  );
  await view.fixture.whenStable();
  const button = screen.getByRole('button', { name: 'Certificado Seleccionar archivo' });
  const input = view.container.querySelector('input[type="file"]') as HTMLInputElement;
  const choose = async (file: File) => {
    fireEvent.change(input, { target: { files: [file] } });
    await view.fixture.whenStable();
  };
  return { view, button, input, choose, fileChange };
}

const certificate = (name = 'empresa.p12', size = 1024) =>
  new File([new Uint8Array(size)], name, { type: 'application/x-pkcs12' });

describe('formatFileSize', () => {
  it('writes bytes as KB or MB in Spanish', () => {
    expect(formatFileSize(512)).toBe('512 B');
    expect(formatFileSize(2048)).toBe('2 KB');
    expect(formatFileSize(1.5 * 1024 * 1024)).toBe('1,5 MB');
  });
});

describe('FileUploadComponent', () => {
  it('is a Spanish choose button labelled by its field, with the file types to pick', async () => {
    const { button, input } = await setUp();

    const label = screen.getByText('Certificado', { selector: 'label' });
    expect(label.getAttribute('for')).toBe(button.id);
    const describedBy = button.getAttribute('aria-describedby')?.split(' ') ?? [];
    expect(describedBy).toContain(screen.getByText('Archivo .p12 o .pfx').id);
    expect(describedBy).toContain(screen.getByText('Ningún archivo seleccionado').id);
    expect(input.accept).toBe('.p12,.pfx');
  });

  it('shows and emits the chosen file without uploading it', async () => {
    const { choose, fileChange } = await setUp();
    const file = certificate();

    await choose(file);

    expect(fileChange).toHaveBeenLastCalledWith(file);
    expect(screen.getByText('empresa.p12')).toBeTruthy();
  });

  it('rejects a file of another type and says why', async () => {
    const { choose, fileChange, button } = await setUp();

    await choose(new File(['x'], 'notas.txt', { type: 'text/plain' }));

    expect(fileChange).toHaveBeenLastCalledWith(null);
    const alert = screen.getByRole('alert');
    expect(alert.textContent?.trim()).toBe(
      '«notas.txt» no es de un tipo permitido. Use un archivo .p12, .pfx.',
    );
    expect(button.getAttribute('aria-describedby')?.split(' ')).toContain(alert.id);
    expect(screen.getByText('Ningún archivo seleccionado')).toBeTruthy();
  });

  it('rejects a file larger than allowed', async () => {
    const { choose, fileChange } = await setUp();

    await choose(certificate('grande.pfx', 4096));

    expect(fileChange).toHaveBeenLastCalledWith(null);
    expect(screen.getByRole('alert').textContent?.trim()).toBe(
      '«grande.pfx» pesa 4 KB y el máximo es 2 KB.',
    );
  });

  it('can be disabled', async () => {
    const { button } = await setUp(true);

    expect((button as HTMLButtonElement).disabled).toBe(true);
  });
});
