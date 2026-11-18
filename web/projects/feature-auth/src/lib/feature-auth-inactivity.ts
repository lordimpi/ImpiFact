/*
 * Punto de entrada perezoso del diálogo de inactividad: `loadInactivityWarning` lo carga con
 * `import('./feature-auth-inactivity')`, en un chunk aparte llamado `feature-auth-inactivity`. Va separado de las
 * páginas porque el diálogo de Optimus UI pesa unos 50 kB y el login no lo usa: solo se baja la primera vez que la
 * sesión avisa por inactividad.
 */
export { InactivityWarningComponent } from './components/inactivity-warning.component';
