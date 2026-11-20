import { API_KEYS_ROLES } from '@impifact/feature-api-keys';
import { COMPANY_ROLES } from '@impifact/feature-companies';
import { ISSUE_SCOPE } from '@impifact/feature-documents';
import { MASTERS_ROLES } from '@impifact/feature-masters';
import { NOTIFICATIONS_ROLES } from '@impifact/feature-notifications';
import { PLANS_ROLES } from '@impifact/feature-plans';
import { PLATFORM_ROLES, PLATFORM_SCOPE } from '@impifact/feature-platform';

/** Main menu entry; without `roles` anyone sees it, with `roles` only a session with one of them (section 7). */
export interface NavItem {
  path: string;
  /** Text key under `nav.*`. */
  label: string;
  /** SVG path (24x24 viewBox, stroked) of the entry icon. */
  icon: string;
  roles?: readonly string[];
  /** Token scope the entry also needs (for example `platform:*`, only present without an active company). */
  scope?: string;
}

/** Section of the sidebar; without `label` it has no heading. Empty sections are hidden. */
export interface NavGroup {
  /** Text key under `navGroups.*`. */
  label?: string;
  items: readonly NavItem[];
}

const DOCUMENTS_ROLES: readonly string[] = ['owner', 'seller'];

const ICONS = {
  home: 'M3 11l9-8 9 8M5 10v10h14V10',
  invoice: 'M6 3h9l4 4v14H6zM14 3v5h5M9 13h7M9 17h5',
  note: 'M5 4h14v16H5zM9 9h6M9 13h6',
  documents: 'M4 6h16M4 12h16M4 18h10',
  customers: 'M16 19v-1a4 4 0 0 0-8 0v1M12 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6M20 19v-1a3 3 0 0 0-2-2.8',
  products: 'M4 8l8-4 8 4v8l-8 4-8-4zM4 8l8 4 8-4M12 12v8',
  company: 'M4 20V6l8-3v17M12 9h8v11M8 9v.01M8 13v.01M16 13v.01M16 17v.01M2 20h20',
  production: 'M5 13l4 4L19 7',
  apiKeys: 'M15 7a4 4 0 1 1-3.9 5H3v3h3v3h3v-3h2.1A4 4 0 0 1 15 7',
  plan: 'M4 6h16v12H4zM4 10h16',
  notifications: 'M18 16V11a6 6 0 0 0-12 0v5l-2 2h16zM10 21h4',
} as const;

/** Sidebar menu, grouped by section and filtered by role. Each feature adds its entries here. */
export const NAV_GROUPS: readonly NavGroup[] = [
  { items: [{ path: '/', label: 'nav.home', icon: ICONS.home }] },
  {
    label: 'navGroups.issue',
    items: [
      {
        path: '/documentos/nuevo',
        label: 'nav.newInvoice',
        icon: ICONS.invoice,
        roles: DOCUMENTS_ROLES,
        scope: ISSUE_SCOPE,
      },
      {
        path: '/documentos/nota-credito',
        label: 'nav.creditNote',
        icon: ICONS.note,
        roles: DOCUMENTS_ROLES,
        scope: ISSUE_SCOPE,
      },
      {
        path: '/documentos/nota-debito',
        label: 'nav.debitNote',
        icon: ICONS.note,
        roles: DOCUMENTS_ROLES,
        scope: ISSUE_SCOPE,
      },
    ],
  },
  {
    label: 'navGroups.query',
    items: [
      {
        path: '/documentos',
        label: 'nav.documents',
        icon: ICONS.documents,
        roles: DOCUMENTS_ROLES,
      },
      { path: '/clientes', label: 'nav.customers', icon: ICONS.customers, roles: MASTERS_ROLES },
      { path: '/productos', label: 'nav.products', icon: ICONS.products, roles: MASTERS_ROLES },
    ],
  },
  {
    label: 'navGroups.settings',
    items: [
      { path: '/empresa', label: 'nav.company', icon: ICONS.company, roles: COMPANY_ROLES },
      {
        path: '/empresa/produccion',
        label: 'nav.production',
        icon: ICONS.production,
        roles: COMPANY_ROLES,
      },
      { path: '/llaves-api', label: 'nav.apiKeys', icon: ICONS.apiKeys, roles: API_KEYS_ROLES },
      { path: '/plan', label: 'nav.plan', icon: ICONS.plan, roles: PLANS_ROLES },
      {
        path: '/notificaciones',
        label: 'nav.notifications',
        icon: ICONS.notifications,
        roles: NOTIFICATIONS_ROLES,
      },
    ],
  },
  {
    label: 'navGroups.platform',
    items: [
      {
        path: '/plataforma/planes',
        label: 'nav.platformPlans',
        icon: ICONS.plan,
        roles: PLATFORM_ROLES,
        scope: PLATFORM_SCOPE,
      },
      {
        path: '/plataforma/empresas',
        label: 'nav.platformCompanies',
        icon: ICONS.company,
        roles: PLATFORM_ROLES,
        scope: PLATFORM_SCOPE,
      },
    ],
  },
];

/**
 * Path of the entry that matches `path` best: the longest entry path that equals it or is a parent segment of it.
 * `/` only matches itself, so the home entry is not active everywhere.
 */
export function activeNavPath(items: readonly NavItem[], path: string): string | null {
  let best: string | null = null;
  for (const item of items) {
    const matches = item.path === path || (item.path !== '/' && path.startsWith(`${item.path}/`));
    if (matches && (best === null || item.path.length > best.length)) {
      best = item.path;
    }
  }
  return best;
}
