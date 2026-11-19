// Falla si la librería api tiene cambios sin commitear después de `npm run api:generate` (archivos modificados,
// borrados o nuevos). Lo usa `npm run api:check` en el CI (integración continua): el cliente generado y el
// openapi.json deben coincidir con lo que produce el backend, para que el contrato no diverja en silencio.
import { execFileSync } from 'node:child_process';

const changes = execFileSync('git', ['status', '--porcelain', '--', 'projects/api'], {
  encoding: 'utf8',
});

if (changes.trim() !== '') {
  console.error(
    'El cliente de API no está al día con el backend. Ejecute `npm run api:generate` y haga commit de:\n' +
      changes,
  );
  process.exit(1);
}

console.log('El cliente de API está al día con el backend.');
