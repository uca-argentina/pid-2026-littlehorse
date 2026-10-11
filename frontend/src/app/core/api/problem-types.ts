/**
 * The stable half of an error from the API. The PWA branches on these and never
 * on the message, which gets reworded, nor on the status code, which is too
 * coarse: several different failures share a 401. See ADR-0009.
 */
export const ProblemTypes = {
  invalidCredentials: 'urn:drinkit:problem:auth:invalid-credentials',
  sessionExpired: 'urn:drinkit:problem:auth:session-expired',
  authenticationRequired: 'urn:drinkit:problem:auth:authentication-required',

  /** The token is good; the role is not the one that screen needs. */
  forbidden: 'urn:drinkit:problem:auth:forbidden',

  /** Right role, but not in the crew of the venue's night (US-35): the administrator adds them. */
  notInTonightsCrew: 'urn:drinkit:problem:auth:not-in-tonights-crew',

  usernameTaken: 'urn:drinkit:problem:staff:username-taken',
  passwordTooShort: 'urn:drinkit:problem:staff:password-too-short',

  /** Taking this account away would leave the venue unable to administer itself. */
  lastAdministrator: 'urn:drinkit:problem:staff:last-administrator',

  /** The address belongs to no venue. A wrong QR, not a network that dropped. */
  venueNotFound: 'urn:drinkit:problem:venue:not-found',

  categoryNameTaken: 'urn:drinkit:problem:category:name-taken',

  productNameTaken: 'urn:drinkit:problem:product:name-taken',

  // US-11. The three that mean the menu moved while somebody was ordering, as
  // opposed to something being wrong with what they sent.
  orderSoldOut: 'urn:drinkit:problem:order:sold-out',
  orderNotOnTheMenu: 'urn:drinkit:problem:order:not-on-the-menu',
  orderStockMoved: 'urn:drinkit:problem:order:stock-moved',

  orderNameRequired: 'urn:drinkit:problem:order:name-required',
  orderNameNeedsSurname: 'urn:drinkit:problem:order:name-needs-surname',
  orderNameOnlyLetters: 'urn:drinkit:problem:order:name-only-letters',
  orderNameTooLong: 'urn:drinkit:problem:order:name-too-long',

  /** No night of the venue is on: the menu reads, confirming is closed (US-35). */
  orderNotTakingOrders: 'urn:drinkit:problem:order:not-taking-orders',

  /** Another screen changed the order in the same instant, and its change stands. */
  orderChangedMeanwhile: 'urn:drinkit:problem:order:changed-meanwhile',

  /** US-23: only an order waiting to be paid at the till can be canceled. */
  orderNotCancelable: 'urn:drinkit:problem:order:not-cancelable',

  /** The till looked up a code this venue does not have. */
  cashierOrderNotFound: 'urn:drinkit:problem:cashier:order-not-found',

  /** Nothing left to collect: paid at the till already, or from the phone. */
  cashierAlreadyPaid: 'urn:drinkit:problem:cashier:already-paid',

  /** Those hours share a moment with another night of the venue. */
  nightOverlaps: 'urn:drinkit:problem:night:overlaps',

  /** One of the chosen accounts is not the venue's any more. */
  nightCrewMemberNotFound: 'urn:drinkit:problem:night:crew-member-not-found',

  /** The night ended while its screen was open: it can no longer change. */
  nightOver: 'urn:drinkit:problem:night:over',

  /** The night began while its screen was open, and the edit moved its start. */
  nightStartLocked: 'urn:drinkit:problem:night:start-locked',

  /** No such night in this venue: a stale link, or another venue's id. */
  nightNotFound: 'urn:drinkit:problem:night:not-found',

  /** A night's stock starts from what the one before it left, so it waits until that one is over (US-37). */
  nightStockPreviousNightNotOver: 'urn:drinkit:problem:night-stock:previous-night-not-over',

  /** The night has no stock of that product: it was never opened, or the id is another venue's. */
  nightStockNotFound: 'urn:drinkit:problem:night-stock:not-found',

  /** Sales left less than an adjustment takes away. */
  nightStockMoved: 'urn:drinkit:problem:night-stock:stock-moved',
} as const;
