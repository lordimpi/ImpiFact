import { eventBlock, radianStage, registeredCodes, tacitAcceptanceBlock } from './radian-events';

const accepted = (...codes: string[]) =>
  codes.map((event_code) => ({ event_code, status: 'accepted' }));

describe('RADIAN event rules', () => {
  it('counts only the events the DIAN accepted, plus the ones it lists', () => {
    const events = [
      { event_code: '030', status: 'accepted' },
      { event_code: '032', status: 'rejected' },
      { event_code: '033', status: 'unconfirmed' },
    ];

    expect([...registeredCodes(events)]).toEqual(['030']);
    expect([...registeredCodes(events, ['032'])].sort()).toEqual(['030', '032']);
  });

  it('allows one event of each type (LGC01)', () => {
    expect(eventBlock('030', registeredCodes(accepted('030')))).toEqual({ reason: 'registered' });
    expect(eventBlock('030', registeredCodes([]))).toBeNull();
  });

  it('lets a rejected event be sent again', () => {
    const rejected = [{ event_code: '030', status: 'rejected' }];

    expect(eventBlock('030', registeredCodes(rejected))).toBeNull();
  });

  it('asks for the acknowledgement before the goods receipt (LGC09)', () => {
    expect(eventBlock('032', registeredCodes([]))).toEqual({ reason: 'needs', code: '030' });
    expect(eventBlock('032', registeredCodes(accepted('030')))).toBeNull();
  });

  it('asks for the acknowledgement and the goods receipt before a claim (LGC03)', () => {
    expect(eventBlock('031', registeredCodes([]))).toEqual({ reason: 'needs', code: '030' });
    expect(eventBlock('031', registeredCodes(accepted('030')))).toEqual({
      reason: 'needs',
      code: '032',
    });
    expect(eventBlock('031', registeredCodes(accepted('030', '032')))).toBeNull();
  });

  it('asks for the acknowledgement and the goods receipt before an express acceptance (LGC12, LGC13)', () => {
    expect(eventBlock('033', registeredCodes(accepted('030')))).toEqual({
      reason: 'needs',
      code: '032',
    });
    expect(eventBlock('033', registeredCodes(accepted('030', '032')))).toBeNull();
  });

  it('never registers a claim and an acceptance on the same invoice (LGC02, LGC04)', () => {
    expect(eventBlock('031', registeredCodes(accepted('030', '032', '033')))).toEqual({
      reason: 'excludedBy',
      code: '033',
    });
    expect(eventBlock('033', registeredCodes(accepted('030', '032', '031')))).toEqual({
      reason: 'excludedBy',
      code: '031',
    });
  });

  it('never registers an express and a tacit acceptance together (LGC05, LGC07)', () => {
    expect(eventBlock('033', registeredCodes(accepted('030', '032'), ['034']))).toEqual({
      reason: 'excludedBy',
      code: '034',
    });
  });

  describe('tacit acceptance (034) on an own invoice', () => {
    it('is offered before asking the DIAN, when the company has not sent it', () => {
      expect(tacitAcceptanceBlock([], null)).toBeNull();
      expect(tacitAcceptanceBlock([{ event_code: '034', status: 'rejected' }], null)).toBeNull();
    });

    it('is sent once', () => {
      expect(tacitAcceptanceBlock(accepted('034'), null)).toEqual({ reason: 'registered' });
    });

    it('needs the goods receipt of the buyer that the DIAN lists (LGC14)', () => {
      expect(tacitAcceptanceBlock([], ['030'])).toEqual({ reason: 'needs', code: '032' });
      expect(tacitAcceptanceBlock([], ['030', '032'])).toBeNull();
    });

    it('is excluded by a claim or an express acceptance the DIAN lists', () => {
      expect(tacitAcceptanceBlock([], ['030', '032', '031'])).toEqual({
        reason: 'excludedBy',
        code: '031',
      });
      expect(tacitAcceptanceBlock([], ['030', '032', '033'])).toEqual({
        reason: 'excludedBy',
        code: '033',
      });
    });
  });

  it('summarizes the furthest accepted step for the list', () => {
    expect(radianStage([])).toBe('none');
    expect(radianStage(accepted('030'))).toBe('acknowledged');
    expect(radianStage(accepted('030', '032'))).toBe('received');
    expect(radianStage(accepted('030', '032', '033'))).toBe('accepted');
    expect(radianStage(accepted('030', '032', '031'))).toBe('claimed');
    expect(radianStage([...accepted('030'), { event_code: '032', status: 'rejected' }])).toBe(
      'acknowledged',
    );
  });
});
