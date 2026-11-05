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

  /** US-54: a product of the promotion is not on the venue's menu. */
  promotionProductNotFound: 'urn:drinkit:problem:promotion:product-not-found',

  promotionNameRequired: 'urn:drinkit:problem:promotion:name-required',

  promotionNameLength: 'urn:drinkit:problem:promotion:name-length',

  promotionDiscountOutOfRange: 'urn:drinkit:problem:promotion:discount-out-of-range',

  promotionDaysRequired: 'urn:drinkit:problem:promotion:days-required',

  promotionWindowEmpty: 'urn:drinkit:problem:promotion:window-empty',

  promotionProductsRequired: 'urn:drinkit:problem:promotion:products-required',

  /** US-59: the card does not have the stamps the reward costs, or the venue switched it off. */
  stampCardNotEnoughStamps: 'urn:drinkit:problem:stamp-card:not-enough-stamps',
  stampCardNotActive: 'urn:drinkit:problem:stamp-card:not-active',

  /** US-59: the reward is off the menu or switched off for tonight. Sold out is the order's own. */
  stampCardRewardUnavailable: 'urn:drinkit:problem:stamp-card:reward-unavailable',

  /** US-59, criterion 4: another phone spent the card first. Nothing was saved. */
  stampCardCardMoved: 'urn:drinkit:problem:stamp-card:card-moved',

  /** US-57: the reward product is not on the venue's menu, or is a combo. */
  stampCardRewardProductNotFound: 'urn:drinkit:problem:stamp-card:reward-product-not-found',
  stampCardRewardIsACombo: 'urn:drinkit:problem:stamp-card:reward-is-a-combo',

  /** US-57: fewer than two stamps, or more than the card can hold. */
  stampCardRuleStampsOutOfRange: 'urn:drinkit:problem:stamp-card-rule:stamps-out-of-range',

  productNameTaken: 'urn:drinkit:problem:product:name-taken',

  /** US-50: one of the combo's products is not on the venue's menu any more. */
  productComboComponentNotFound: 'urn:drinkit:problem:product:combo-component-not-found',

  productComboInsideCombo: 'urn:drinkit:problem:product:combo-inside-combo',

  /** US-39: the number belongs to a table of the venue, put away or not. */
  tableNumberTaken: 'urn:drinkit:problem:table:number-taken',

  /** Sales left less stock than an adjustment of the night's stock takes away (US-37). */
  productStockMoved: 'urn:drinkit:problem:product:stock-moved',

  // US-11. The three that mean the menu moved while somebody was ordering, as
  // opposed to something being wrong with what they sent.
  orderSoldOut: 'urn:drinkit:problem:order:sold-out',
  orderNotOnTheMenu: 'urn:drinkit:problem:order:not-on-the-menu',
  orderStockMoved: 'urn:drinkit:problem:order:stock-moved',

  /**
   * US-55: the total the customer saw is not the total now — a promotion
   * started or ended while the cart was open. Nothing was charged.
   */
  orderPriceChanged: 'urn:drinkit:problem:order:price-changed',

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
