import {
  emptySeller,
  emptySupportLine,
  sellerBody,
  todayInColombia,
} from './support-document.form';

describe('support document form', () => {
  it('takes today in Colombia, five hours behind UTC', () => {
    expect(todayInColombia(new Date('2026-11-22T03:00:00Z'))).toBe('2026-11-21');
    expect(todayInColombia(new Date('2026-11-22T06:00:00Z'))).toBe('2026-11-22');
  });

  it('starts each line without IVA', () => {
    expect(emptySupportLine().taxPercent).toBe('0');
  });

  it('sends the postal code as text and the foreign place only for a non-resident seller', () => {
    const seller = {
      ...emptySeller(),
      identificationTypeId: '6',
      identificationNumber: ' 800000003 ',
      name: 'PROVEEDOR',
      postalZoneCode: '050001',
      countryId: '249',
      stateName: 'Florida',
      municipalityName: 'Miami',
    };

    expect(sellerBody(seller, false)).toEqual({
      type_document_identification_id: 6,
      identification_number: '800000003',
      name: 'PROVEEDOR',
      postal_zone_code: '050001',
    });
    expect(sellerBody(seller, true)).toEqual({
      type_document_identification_id: 6,
      identification_number: '800000003',
      name: 'PROVEEDOR',
      postal_zone_code: '050001',
      country_id: 249,
      state_name: 'Florida',
      municipality_name: 'Miami',
    });
  });
});
