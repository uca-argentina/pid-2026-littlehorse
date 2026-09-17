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

  usernameTaken: 'urn:drinkit:problem:staff:username-taken',
  passwordTooShort: 'urn:drinkit:problem:staff:password-too-short',

  /** Taking this account away would leave the venue unable to administer itself. */
  lastAdministrator: 'urn:drinkit:problem:staff:last-administrator',

  /** The address belongs to no venue. A wrong QR, not a network that dropped. */
  venueNotFound: 'urn:drinkit:problem:venue:not-found',

  productNameTaken: 'urn:drinkit:problem:product:name-taken',
} as const;
