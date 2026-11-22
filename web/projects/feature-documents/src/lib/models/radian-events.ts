/*
 * RADIAN events (Anexo Técnico 1.9, section 8.6, rules LGC01 to LGC14) as the API applies them in
 * `RadianEventRules.cs`. The panel uses them only to enable or explain each action: the API checks the same order
 * again and answers 422 with its reason, which the page shows as it comes.
 */

export const ACKNOWLEDGEMENT = '030';
export const CLAIM = '031';
export const GOODS_RECEIPT = '032';
export const EXPRESS_ACCEPTANCE = '033';
export const TACIT_ACCEPTANCE = '034';

/** Events the buyer registers on a received invoice, in the order of the flow. */
export const RECEIVED_EVENT_CODES = [
  ACKNOWLEDGEMENT,
  GOODS_RECEIPT,
  EXPRESS_ACCEPTANCE,
  CLAIM,
] as const;

export type ReceivedEventCode = (typeof RECEIVED_EVENT_CODES)[number];
export type RadianEventCode = ReceivedEventCode | typeof TACIT_ACCEPTANCE;

/** Claim reasons of a 031 (`RadianCatalog.ClaimReasons`); their names are under `documents.radian.claimReasons`. */
export const CLAIM_REASONS = ['01', '02', '03', '04'] as const;

/**
 * Why an action is not available, as a key under `documents.radian.blocked`. `registered` is the event already
 * registered; `needs` and `excludedBy` name the event that is missing or that excludes it.
 */
export type EventBlock =
  | { reason: 'registered' }
  | { reason: 'needs'; code: string }
  | { reason: 'excludedBy'; code: string };

/** Events that need others before them (LGC09, LGC03, LGC12, LGC13, LGC14). */
const PREVIOUS: Record<string, readonly string[]> = {
  [GOODS_RECEIPT]: [ACKNOWLEDGEMENT],
  [CLAIM]: [ACKNOWLEDGEMENT, GOODS_RECEIPT],
  [EXPRESS_ACCEPTANCE]: [ACKNOWLEDGEMENT, GOODS_RECEIPT],
  [TACIT_ACCEPTANCE]: [GOODS_RECEIPT],
};

/** Events that exclude each other (LGC02, LGC04, LGC05, LGC07 and Tabla 8). */
const EXCLUDING: Record<string, readonly string[]> = {
  [CLAIM]: [EXPRESS_ACCEPTANCE, TACIT_ACCEPTANCE],
  [EXPRESS_ACCEPTANCE]: [CLAIM, TACIT_ACCEPTANCE],
  [TACIT_ACCEPTANCE]: [CLAIM, EXPRESS_ACCEPTANCE],
};

/**
 * Codes of the events already registered: only the ones the DIAN accepted (a rejected or unconfirmed one can be sent
 * again, as the API allows), plus the codes the DIAN lists for the invoice when the person asked for them.
 */
export function registeredCodes(
  events: readonly { event_code: string; status: string }[] | null | undefined,
  dianCodes: readonly string[] = [],
): ReadonlySet<string> {
  const accepted = (events ?? [])
    .filter((event) => event.status === 'accepted')
    .map((event) => event.event_code);
  return new Set([...accepted, ...dianCodes]);
}

/**
 * Whether the event `code` can be registered now, following the order of the API: one event of each type, the
 * previous ones first, and a claim and an acceptance never together. Returns `null` when it can, or why not.
 */
export function eventBlock(code: string, registered: ReadonlySet<string>): EventBlock | null {
  if (registered.has(code)) {
    return { reason: 'registered' };
  }
  const missing = (PREVIOUS[code] ?? []).find((previous) => !registered.has(previous));
  if (missing) {
    return { reason: 'needs', code: missing };
  }
  const excluding = (EXCLUDING[code] ?? []).find((other) => registered.has(other));
  if (excluding) {
    return { reason: 'excludedBy', code: excluding };
  }
  return null;
}

/**
 * Whether the tacit acceptance (034) can be offered on an own invoice. Only an accepted sale invoice takes it, and the
 * company sends it once; the 032 of the buyer and the absence of a claim or an express acceptance are known only after
 * asking the DIAN (`dianCodes`), so before that the API decides. The 3 business days after the 032 are checked by
 * the API too.
 */
export function tacitAcceptanceBlock(
  ownEvents: readonly { event_code: string; status: string }[] | null | undefined,
  dianCodes: readonly string[] | null,
): EventBlock | null {
  const registered = registeredCodes(ownEvents, dianCodes ?? []);
  if (registered.has(TACIT_ACCEPTANCE)) {
    return { reason: 'registered' };
  }
  if (dianCodes === null) {
    return null;
  }
  return eventBlock(TACIT_ACCEPTANCE, registered);
}

/** Summary of a received invoice for the list: the furthest accepted step of its flow. */
export type RadianStage =
  'claimed' | 'accepted' | 'tacitlyAccepted' | 'received' | 'acknowledged' | 'none';

export function radianStage(
  events: readonly { event_code: string; status: string }[] | null | undefined,
): RadianStage {
  const registered = registeredCodes(events);
  if (registered.has(CLAIM)) return 'claimed';
  if (registered.has(EXPRESS_ACCEPTANCE)) return 'accepted';
  if (registered.has(TACIT_ACCEPTANCE)) return 'tacitlyAccepted';
  if (registered.has(GOODS_RECEIPT)) return 'received';
  if (registered.has(ACKNOWLEDGEMENT)) return 'acknowledged';
  return 'none';
}

/** Text key of an event name (`documents.radian.events.*`); an unknown code keeps the code. */
export function eventNameKey(code: string | null | undefined): string {
  return ['030', '031', '032', '033', '034'].includes(code ?? '')
    ? `radian.events.${code}`
    : 'radian.events.unknown';
}
