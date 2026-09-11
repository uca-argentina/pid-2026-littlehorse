/**
 * The stable half of an error from the API. The PWA branches on these and never
 * on the message, which gets reworded, nor on the status code, which is too
 * coarse: several different failures share a 401. See ADR-0009.
 */
export const ProblemTypes = {
  invalidCredentials: 'urn:drinkit:problem:auth:invalid-credentials',
  sessionExpired: 'urn:drinkit:problem:auth:session-expired',
  authenticationRequired: 'urn:drinkit:problem:auth:authentication-required',
} as const;
