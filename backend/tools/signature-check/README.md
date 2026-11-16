# Verificación independiente de la firma

Comprueba con `xmlsec1` (libxmlsec1 sobre OpenSSL) que la firma XAdES-EPES de `ImpiFact.Dian` es válida con una implementación distinta de .NET. Es la puerta de salida de la fase 1a: «firma válida con una librería independiente».

## Requisitos

- Docker. El script usa la imagen `alpine:3.20` e instala `xmlsec` al arrancar, así que necesita acceso a los repositorios de Alpine.
- .NET 10 para generar las muestras.

## Uso

Los comandos se ejecutan desde la carpeta `backend/`.

1. Genere las muestras en un directorio **fuera del repositorio**. La herramienta se niega a escribir dentro del repositorio.

   ```bash
   dir="$(mktemp -d)"
   dotnet run --project tools/ImpiFact.Prototype -- sign-samples --out "$dir"
   ```

   Sin `--out` usa una carpeta temporal nueva y la muestra al terminar. Escribe en `$dir/signed/` cada caso de referencia (golden) firmado (facturas, notas crédito y nota débito) y en `$dir/signer.pem` el certificado público de prueba. El certificado se genera en memoria en cada corrida y la llave privada nunca se escribe.

2. Verifique las firmas:

   ```bash
   bash tools/signature-check/verify.sh "$dir"
   ```

   Para cada archivo, el script ejecuta:

   ```bash
   xmlsec1 --verify --trusted-pem signer.pem \
     --id-attr:Id "http://uri.etsi.org/01903/v1.3.2#:SignedProperties" <archivo>
   ```

   Luego altera `cbc:PayableAmount` en una copia y comprueba que esa copia se rechaza. Termina con código 0 solo si todas las firmas son válidas y todas las copias alteradas se rechazan.

3. Borre `$dir` al terminar. Nada de lo generado debe quedar en el repositorio.

## Notas

- `--id-attr:Id` hace falta porque `xades:SignedProperties` usa un atributo `Id` sin esquema que lo declare como identificador. Las referencias a `ds:KeyInfo` y al documento (`URI=""`) se resuelven sin ayuda.
- `--trusted-pem` confía directamente en el certificado autofirmado de prueba. Con un certificado real habría que pasar la cadena de la entidad certificadora.
- Si `apk` falla con `UNTRUSTED signature`, es un fallo transitorio del espejo de Alpine. Vuelva a ejecutar el script.
- En Windows, use Git Bash. El script convierte la ruta con `pwd -W` y desactiva la conversión de rutas de MSYS para el contenedor.
