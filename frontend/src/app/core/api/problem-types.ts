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

  /** US-39: the number belongs to a table of the venue, put away or not. */
  tableNumberTaken: 'urn:drinkit:problem:table:number-taken',

  /** Sales left less stock than an adjustment of the night's stock takes away (US-37). */
  productStockMoved: 'urn:drinkit:problem:product:stock-moved',

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

  /** US-43: another waiter took the table first; the list reloads with whose it is. */
  waiterAlreadyTaken: 'urn:drinkit:problem:waiter:already-taken',

  /** US-44: only the waiter carrying a table hands it over. */
  waiterNotYours: 'urn:drinkit:problem:waiter:not-yours',

  /** US-23: only an order waiting to be paid at the till can be canceled. */
  orderNotCancelable: 'urn:drinkit:problem:order:not-cancelable',

  /** The till looked up a code this venue does not have. */
  cashierOrderNotFound: 'urn:drinkit:problem:cashier:order-not-found',

  /** Nothing left to collect: paid at the till already, or from the phone. */
  cashierAlreadyPaid: 'urn:drinkit:problem:cashier:already-paid',

  /** US-36: cash takes its drinks when collected, and by then no bar has one of them. */
  cashierSoldOut: 'urn:drinkit:problem:cashier:sold-out',

  /** The order belongs to a night that is over: nothing to collect it against. */
  cashierNightOver: 'urn:drinkit:problem:cashier:night-over',

  /** US-36: a real order of the venue, scanned at a bar it did not go to. */
  kdsAtAnotherBar: 'urn:drinkit:problem:kds:at-another-bar',

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

  /** US-37: the night ended while its stock was open, and a finished night's stock is only read. */
  nightStockClosed: 'urn:drinkit:problem:night:stock-closed',

  /** US-37: loading a bar takes from the storeroom, and it has fewer than that. */
  nightStoreroomShort: 'urn:drinkit:problem:night:storeroom-short',

  /** US-41: the table's code opens nothing tonight — ran out, regenerated, invented or another venue's. */
  vipAccountNotFound: 'urn:drinkit:problem:vip-account:not-found',

  /**
   * US-42: the difference the screen showed is not the difference any more —
   * another phone of the table spent meanwhile. Nothing was charged.
   */
  vipAccountBalanceChanged: 'urn:drinkit:problem:vip-account:balance-changed',

  /** US-40: a night selling VIP tables needs at least one waiter in its crew. */
  nightWaiterRequired: 'urn:drinkit:problem:night:waiter-required',

  /** US-41: a phone of one of the night's tables spent while its form was open. */
  nightTablesMoved: 'urn:drinkit:problem:night:tables-moved',

  /** US-40: one of the chosen tables is not the venue's any more. */
  nightTableNotFound: 'urn:drinkit:problem:night:table-not-found',

  /** US-40: one of the chosen tables was put away while the form was open. */
  vipAccountTableInactive: 'urn:drinkit:problem:vip-account:table-inactive',

  /** US-40: a table that already spent part of its budget stays in the night. */
  vipAccountHasSpending: 'urn:drinkit:problem:vip-account:has-spending',

  /** US-39: the table is sold in a night that has not ended, so it is not put away. */
  tableInANightNotOver: 'urn:drinkit:problem:table:in-a-night-not-over',

  /** US-37: a KDS still holding stock in the night cannot be taken out of its crew. */
  nightBarHoldsStock: 'urn:drinkit:problem:night:bar-holds-stock',

  /** US-37: the bar was taken out of the night's crew while its stock was open. */
  nightBarNotInNight: 'urn:drinkit:problem:night:bar-not-in-night',
} as const;
