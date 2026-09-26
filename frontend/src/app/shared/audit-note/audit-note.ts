import { Component, computed, input } from '@angular/core';
import type { components } from '../../core/api/schema';

type Audit = components['schemas']['AuditResponse'];

/**
 * When it is written for somebody at a bar: day and time in the venue's own
 * clock, which is the device's. Built once rather than per call.
 */
const WHEN = new Intl.DateTimeFormat('es-AR', { dateStyle: 'medium', timeStyle: 'short' });

/**
 * US-30: who made something and who last touched it, at the foot of the ficha.
 * A null is not the same thing everywhere, and the screen tells them apart: no
 * date at all is a row from before the audit existed ("sin registro"), and a
 * date with no author is something nobody signed in wrote.
 */
@Component({
  selector: 'drinkit-audit-note',
  styleUrl: './audit-note.scss',
  templateUrl: './audit-note.html',
})
export class AuditNote {
  /**
   * Undefined while the row is still arriving. No line ends in a period: the
   * date does ("p. m."), and two of them in a row read as a typo.
   */
  readonly audit = input<Audit | undefined>(undefined);

  protected readonly created = computed(() => {
    const audit = this.audit();

    if (audit?.createdAt == null) return 'Sin registro de quién lo creó ni de cuándo';

    return `Creado${byline(audit.createdBy)} el ${WHEN.format(new Date(audit.createdAt))}`;
  });

  protected readonly modified = computed(() => {
    const audit = this.audit();

    if (audit?.lastModifiedAt != null)
      return `Última modificación${byline(audit.lastModifiedBy)} el ${WHEN.format(new Date(audit.lastModifiedAt))}`;

    return audit?.createdAt == null
      ? 'Sin registro de modificaciones'
      : 'Sin modificaciones desde que se creó';
  });
}

function byline(author: string | null | undefined): string {
  return author == null ? '' : ` por ${author}`;
}
