# Colección Postman de ApiDian (copia limpia)

`apidian-v2.1.postman_collection.json` es la colección oficial **FacturaLatam ApiDianV2.1** (185 solicitudes en 14 carpetas), el contrato contra el que se prueba ImpiFact (PRD, sección 9). Las pruebas de contrato leen esta copia, así que nunca usan la red.

- Fuente: <https://documenter.getpostman.com/view/50442293/2sBYHUSfRJ>, descargada de `https://documenter.gw.postman.com/api/collections/50442293/2sBYHUSfRJ?segregateAuth=true&versionTag=latest`.
- Fecha de descarga: 17-nov-2026 (la colección dice `publishDate` 22-oct-2026).

## Qué se quitó

La colección publicada trae valores con aspecto de credenciales reales y datos de contacto. `sanitize.py` los quita y deja todo lo demás igual:

- Tokens de las variables y de la autenticación Bearer, encabezados `Authorization`, `X-Admin-Token` y `Cookie`.
- Valores de claves con `token`, `password` o `secret` en el nombre, `certificate`, `pin` y `mail_username` (el certificado `.p12` de ejemplo y su clave incluidos).
- Correos (pasan a `contacto@example.com`) y teléfonos (pasan a `3000000000`).
- Textos en base64 de 200 caracteres o más (XML firmados, ZIP, PDF y logos de las respuestas de ejemplo).
- Las carpetas 11 a 14 (Enterprise, nunca se ejecutan) quedan sin cuerpos ni respuestas de ejemplo.

## Actualizar la copia

```bash
curl -sS -o descargada.json "https://documenter.gw.postman.com/api/collections/50442293/2sBYHUSfRJ?segregateAuth=true&versionTag=latest"
python -I sanitize.py descargada.json apidian-v2.1.postman_collection.json
```

Después, correr `CollectionSnapshotTests` (conteo por carpeta y ausencia de credenciales) y la prueba de contrato.
