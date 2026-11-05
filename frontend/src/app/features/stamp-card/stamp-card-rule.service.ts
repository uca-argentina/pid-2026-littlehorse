import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type StampCardRule = components['schemas']['StampCardRuleResponse'];

export type StampCardRuleData = components['schemas']['StampCardRuleRequest'];

/** Staff take their venue from the token: no slug in the path, like the products. */
export const STAMP_CARD_RULE_URL = '/api/staff/stamp-card';

/** The least and the most stamps a reward can take, as the server enforces them. */
export const MOST_STAMPS = 50;

export const LEAST_STAMPS = 2;

@Injectable({ providedIn: 'root' })
export class StampCardRuleService {
  private readonly http = inject(HttpClient);

  /** US-57: sets the card up, or replaces its rule. Customers keep their stamps. */
  save(rule: StampCardRuleData): Observable<StampCardRule> {
    return this.http.put<StampCardRule>(STAMP_CARD_RULE_URL, rule);
  }
}
