import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type VipTable = components['schemas']['TableResponse'];

export type NewVipTable = components['schemas']['CreateTableRequest'];

export type VipTableDescription = components['schemas']['UpdateTableRequest'];

/**
 * No venue in the path: everything here is done with a token, and the token
 * carries the venue. Putting it in the URL would offer a knob that must never
 * be turned.
 */
export const TABLES_URL = '/api/staff/tables';

/** Kept in step with Table.NumberMaxValue in the domain. */
export const TABLE_NUMBER_MAX = 999;

/** Kept in step with Table.CapacityMaxValue in the domain. */
export const TABLE_CAPACITY_MAX = 50;

/** Kept in step with Table.LocationMaxLength in the domain. */
export const TABLE_LOCATION_MAX_LENGTH = 60;

@Injectable({ providedIn: 'root' })
export class TablesService {
  private readonly http = inject(HttpClient);

  create(table: NewVipTable): Observable<VipTable> {
    return this.http.post<VipTable>(TABLES_URL, table);
  }

  /** People and location only: the number is what the orders placed from it say. */
  update(id: string, description: VipTableDescription): Observable<VipTable> {
    return this.http.put<VipTable>(`${TABLES_URL}/${id}`, description);
  }

  /**
   * POST and not DELETE, because nothing is deleted. The row stays so the
   * orders placed from that table keep saying "Mesa 5".
   */
  deactivate(id: string): Observable<VipTable> {
    return this.http.post<VipTable>(`${TABLES_URL}/${id}/deactivate`, {});
  }

  reactivate(id: string): Observable<VipTable> {
    return this.http.post<VipTable>(`${TABLES_URL}/${id}/reactivate`, {});
  }
}
