/*
 * RADIAN fixtures of the feature specs (only the .spec.ts files use them; not in the public API).
 */

/** A received invoice as the API returns it, with its acknowledgement (030) and goods receipt (032) accepted. */
export const RECEIVED = {
  id: 'rd-1',
  supplier_identification_number: '800000003',
  supplier_dv: '4',
  supplier_name: 'PROVEEDOR DE EJEMPLO SAS',
  supplier_email: 'facturas@proveedor.example.com',
  type_document_id: 1,
  prefix: 'FV',
  number: 'FV1001',
  cufe: 'a'.repeat(96),
  issue_date: '2026-11-18',
  type_environment_id: 2,
  line_extension_amount: '1000000.00',
  tax_exclusive_amount: '1000000.00',
  allowance_total_amount: '0.00',
  tax_amount: '190000.00',
  payable_amount: '1190000.00',
  created_at: '2026-11-19T15:30:00Z',
  events: [
    {
      id: 'ev-1',
      event_code: '030',
      event_name: 'Acuse de recibo de Factura Electrónica de Venta',
      status: 'accepted',
      cude: 'c'.repeat(96),
      number: 'AR1',
      issued_at: '2026-11-19T15:40:00Z',
    },
    {
      id: 'ev-2',
      event_code: '032',
      event_name: 'Recibo del bien y/o prestación del servicio',
      status: 'accepted',
      cude: 'd'.repeat(96),
      number: 'RB1',
      issued_at: '2026-11-19T15:45:00Z',
    },
  ],
};
