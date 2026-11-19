/**
 * `returnUrl` llega en la URL y cualquiera puede escribirlo: solo se aceptan rutas internas del panel (empiezan por
 * una sola `/`), nunca otra dirección (`//otro.sitio`, `https://…`, `/\otro.sitio`). Lo demás da `null`.
 */
export function safeReturnUrl(value: string | null | undefined): string | null {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return null;
  }
  return value;
}
