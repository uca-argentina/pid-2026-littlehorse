# Guía: aplicar un cambio en el frontend

Mismo ejemplo que la guía del backend: **el cliente califica su pedido entregado** desde la pantalla
de seguimiento.

**Prerrequisito:** el backend ya funciona y lo probaste en Scalar.

---

## 0. Cómo está armado (repaso rápido)

Es la misma idea de capas que el backend:

| Backend | Front | Archivo | Qué hace |
|---|---|---|---|
| Endpoint | **Page** | `pages/xxx.page.ts` + `.html` + `.scss` | Lo que se ve. Capa fina. |
| Handler | **Store** | `xxx.store.ts` | Estado de la pantalla (signals) + lógica. |
| Repository | **Service** | `xxx.service.ts` | Llamadas HTTP. No decide nada. |
| DTO | **`schema.d.ts`** | `core/api/schema.d.ts` | Tipos **generados** del OpenAPI. |
| `MapGroup` | **Routes** | `xxx.routes.ts` + `app.routes.ts` | Qué URL abre qué página. |
| Middleware | **Interceptors / Guards** | `core/auth/` | Token en cada request / quién entra. |
| `AddScoped` | **`inject()`** | — | Pedir una dependencia. |

**El viaje de un clic:**

```
.html (click)="store.rate(...)"
  → store: ratingNow.set(true); service.rate(...).subscribe(...)
    → service: http.post('/api/...')
      → interceptors (token) → proxy.conf.json (/api → https://localhost:7176, strips the /api)
        → BACKEND → 204
    ← store: next → ratedAlready.set(true)   |   error → rateRefusal.set('...')
← the signals changed → Angular redraws on its own
```

**Signals en 4 líneas:**

```ts
const x = signal(0);                    // create
x();                                    // read (WITH parentheses, in the .html too)
x.set(5);                               // write
const y = computed(() => x() * 2);      // derived, recalculated on its own
```

**Plantilla en 5 reglas:** `{{ x() }}` muestra · `[prop]="x()"` pasa un valor · `(click)="f()"`
reacciona · `@if (...) { } @else { }` · `@for (x of xs; track x.id) { }`.

---

## 1. Por dónde empezar

### Paso 0: encontrar la pantalla (antes de escribir nada)

El front se arma **de abajo hacia arriba**, como el backend, pero **primero hay que encontrar dónde va**.
Siempre se empieza por la **URL**:

1. **Abrí la pantalla en el navegador** y mirá la URL. Por ejemplo, `/bar-alfa/orders/A-0332/9f3c...`
   o `/bar-alfa/staff/reports`.
2. **Buscá esa ruta en `src/app/app.routes.ts`.** Por ejemplo, `':venueSlug/orders'` → `loadChildren`
   apunta a `features/tracking/tracking.routes.ts`. **Ahí está la carpeta de la feature.**
3. **Abrí el `xxx.routes.ts` de esa carpeta** → `loadComponent` te dice qué **página** es
   (`pages/tracking.page.ts`).
4. **Abrí la página (`.ts` + `.html`)** y mirá:
   - ¿usa un **store** (`inject(XxxStore)`, `providers: [XxxStore]`)?
   - ¿usa **`httpResource`** directo?
   - ¿qué **service** importa?

Con eso ya sabés **qué archivos vas a tocar**. Si la pantalla no existe, buscá la más parecida y
copiá su estructura (ver "Pantalla nueva" en la sección 6).

### Paso 1: hacerse 4 preguntas

1. **¿Es lectura o escritura?** (Sección 2.) **Define todo lo demás.**
2. **¿Qué pantalla?** ¿Existe (agregar un bloque) o es nueva (ruta + página)?
3. **¿Cuándo se muestra?** **Leé el store antes de diseñar.**
   - Ejemplo real: el seguimiento devuelve **404 cuando el pedido está entregado**. El store lo traduce
     así: si ya había mostrado el pedido → `'over'`; si nunca → `'nowhere'`. El formulario va con
     **`status() === 'over'`**, no con `'following'`.
4. **¿Quién lo usa?** Cliente (anónimo, `anonymously()`, ruta con `:venueSlug`) o personal (token,
   guards, rutas `/staff/...`).

### Paso 2: el orden para escribir

| # | Qué | Por qué en este orden |
|---|---|---|
| 1 | **`schema.d.ts`** (`generate:api` + Prettier) | Sin los tipos, nada de lo que sigue compila |
| 2 | **Service** (sólo en escritura) | El store lo necesita |
| 3 | **Store** (sólo en escritura con estado) | La página lo necesita |
| 4 | **Page `.ts`** | Lo que el `.html` va a leer |
| 5 | **Page `.html`** | Lo último: dibuja lo que ya existe |
| 6 | **Routes** (sólo si la pantalla es nueva) | |

Compilá (`pnpm --prefix frontend run build`) **después de cada paso**, no al final.

---

## 2. Lectura vs escritura

**Es la primera decisión.** Cambia qué archivos tocás y qué herramienta de Angular usás.

| | **Lectura** (mostrar datos) | **Escritura** (mandar datos / hacer algo) |
|---|---|---|
| **Ejemplo** | Mostrar el promedio en el reporte | Calificar el pedido |
| **HTTP** | `GET` | `POST` / `PUT` / `DELETE` |
| **Cuándo se dispara** | **Sola**, al abrir la pantalla (o al cambiar un parámetro) | Cuando el usuario **hace algo** (clic en "Enviar") |
| **Herramienta de Angular** | **`httpResource`** (Resource API) | **`http.post(...)` + `.subscribe()`** |
| **Dónde va** | Directo en la **página** | **Service** (la llamada) + **store** (el estado) |
| **Estados que manejás** | Los da gratis: `isLoading()`, `error()`, `value()`, `hasValue()` | Los escribís vos: `sending`, `done`, `problem` |
| **Backend** | Query con `AsNoTracking()` → DTO | Repositorio → agregado → `SaveAsync` |
| **Archivos que tocás** | `schema.d.ts` + página (`.ts` y/o `.html`) | `schema.d.ts` + service + store + página + `problem-types.ts` |

### Lectura: `httpResource`

```ts
// reports.page.ts — already exists
protected readonly report = httpResource<NightMetrics>(() => {
  const id = this.nightId();

  return id === undefined ? undefined : metricsUrl(id);   // undefined = "don't ask yet"
});
```

- **Se dispara sola** cuando se crea la página, y **vuelve a pedir sola** si cambia un signal que usa
  adentro (acá, `nightId()`).
- **No hay `subscribe`, ni store, ni service con método**: el service sólo exporta la URL y el tipo.
- En el `.html`:

```html
@if (report.isLoading()) {
  <p class="note" role="status">Cargando…</p>
} @else if (report.error()) {
  <p class="note note-bad" role="alert">No pudimos traer el reporte.</p>
} @else if (report.hasValue()) {
  @let r = report.value();
  <span>{{ r.sold }}</span>
}
```

**Ejemplo: agregar el promedio de calificaciones al reporte.** Como `report` ya trae
`NightMetricsResponse` y el backend le sumó `averageRating`, **sólo hace falta**:

1. `generate:api` + Prettier → `averageRating: number | null` aparece en el tipo.
2. En `reports.page.html`, adentro del `@if (report.hasValue() ...)`, un bloque más copiando un `kpi`:

```html
<div class="kpi">
  <span class="label">Calificación</span>
  <span class="value">
    {{ r.averageRating === null ? 'Sin calificar' : (r.averageRating | number: '1.1-1') + ' ★' }}
  </span>
</div>
```

(El pipe `number` necesita `DecimalPipe` en los `imports` del componente. Otra opción es un método
`stars(value)` en el `.ts` que haga `value.toFixed(1)`.)

**Una lectura nueva no toca service ni store.** Si la pantalla ya lee ese endpoint, es
**sólo HTML**.

### Escritura: service + store + `subscribe`

Es el caso de las secciones 4 a 6 (calificar). Resumen del patrón:

```ts
// store
rate(stars: number, comment: string | null): void {
  if (this.ratingNow()) return;                 // 1. no double tap
  this.ratingNow.set(true);                     // 2. "sending…"

  this.service.rate(..., { stars, comment })    // 3. the call (the service builds the URL)
    .pipe(takeUntilDestroyed(this.destroyRef))  // 4. required with subscribe
    .subscribe({
      next: () => { /* 5a. OK: clear "sending", mark "done" */ },
      error: (error) => { /* 5b. failed: clear "sending", save why (problemTypeOf) */ },
    });
}
```

**¿Por qué la escritura no usa `httpResource`?** Porque un resource **se dispara solo** al crearse.
Un `POST` tiene que salir **sólo cuando el usuario lo pide**, y una sola vez.

### Cómo darte cuenta cuál es en la consigna

- **"Ver", "mostrar", "listar", "el admin ve..."** → lectura.
- **"Cargar", "crear", "marcar", "calificar", "cancelar", "el cliente puede..."** → escritura.
- Muchas consignas tienen **las dos**. La calificación: el cliente **califica** (escritura) y el admin
  **ve el promedio** (lectura). Hacé primero la escritura (genera los datos) y después la lectura.

---

## 3. Contrato — `schema.d.ts`

Con la API corriendo:

```bash
pnpm --prefix frontend run generate:api
pnpm --prefix frontend exec prettier --write src/app/core/api/schema.d.ts   # ← ALWAYS afterwards
git diff --stat frontend/src/app/core/api/schema.d.ts                       # should be few lines
```

Buscá tu DTO nuevo (`RateOrderRequest`) y verificá los campos. **Nunca escribas interfaces de DTOs a
mano**: usá `components['schemas']['Nombre']`.

---

## 4. Service — la llamada HTTP (escritura)

**Archivo:** `features/<feature>/<feature>.service.ts` · **Molde:** el método más parecido del
mismo service (para calificar: `cancel`).

```ts
export type RateOrderRequest = components['schemas']['RateOrderRequest'];

export function rateOrderUrl(venueSlug: string, code: string, token: string): string {
  return `${trackingUrl(venueSlug, code, token)}/rate`;
}

// inside the class:
rate(venueSlug: string, code: string, token: string, request: RateOrderRequest) {
  return this.http.post<void>(rateOrderUrl(venueSlug, code, token), request, {
    context: anonymously(),   // client: does NOT send a staff token even if a session is open on that phone
  });
}
```

- Las URLs empiezan con `/api` (el proxy lo saca). Cliente: `/api/{venueSlug}/...`. Personal:
  `/api/staff/...` (sin slug).
- Devuelve un `Observable`: **la llamada no sale hasta que alguien hace `.subscribe()`**.

---

## 5. Store — el estado (escritura)

**Archivo:** `features/<feature>/<feature>.store.ts` · **Molde:** `cancel()` y sus signals.

**a) Los motivos de error**, junto a los otros `type`:

```ts
export type RateProblem = 'alreadyRated' | 'failed';
```

**b) Los signals:** privado escribible + público de sólo lectura, con una línea en blanco entre cada uno:

```ts
private readonly ratingNow = signal(false);

readonly rating = this.ratingNow.asReadonly();            // while it's being sent

private readonly ratedAlready = signal(false);

readonly rated = this.ratedAlready.asReadonly();          // finished OK

private readonly rateRefusal = signal<RateProblem | null>(null);

readonly rateProblem = this.rateRefusal.asReadonly();     // why it failed
```

**c) El método:**

```ts
rate(stars: number, comment: string | null): void {
  if (this.where === null || this.ratingNow()) return;   // no double tap

  this.ratingNow.set(true);
  this.rateRefusal.set(null);

  this.tracking
    .rate(this.where.venueSlug, this.where.code, this.where.token, { stars, comment })
    .pipe(takeUntilDestroyed(this.destroyRef))            // REQUIRED with subscribe
    .subscribe({
      next: () => {
        this.ratingNow.set(false);
        this.ratedAlready.set(true);
      },
      error: (error: unknown) => {
        this.ratingNow.set(false);
        this.rateRefusal.set(
          problemTypeOf(error) === ProblemTypes.orderAlreadyRated ? 'alreadyRated' : 'failed',
        );
      },
    });
}
```

**d) Un error de negocio nuevo** → agregarlo en `core/api/problem-types.ts`. El string es
`urn:drinkit:problem:` + el código del backend con `.` → `:` y `_` → `-`:
`order.already_rated` → `urn:drinkit:problem:order:already-rated`.

---

## 6. Page — lo que se ve

### `.ts`: sólo lo que es de la pantalla

```ts
protected readonly stars = signal(0);

protected readonly comment = signal('');

protected readonly starChoices = [1, 2, 3, 4, 5];

protected readonly canRate = computed(
  () => this.store.status() === 'over' && !this.store.rated(),
);

protected sendRating(): void {
  const written = this.comment().trim();

  this.store.rate(this.stars(), written === '' ? null : written);
}

protected onComment(event: Event): void {
  this.comment.set((event.target as HTMLTextAreaElement).value);   // typed, without $any
}
```

### `.html`: textos en español, de vos

```html
@if (canRate()) {
  <section class="rating" aria-label="Calificá tu pedido">
    <h2>¿Qué tal estuvo?</h2>

    <div role="group" aria-label="Estrellas">
      @for (star of starChoices; track star) {
        <button type="button" [attr.aria-pressed]="stars() === star" (click)="stars.set(star)">
          {{ star }}★
        </button>
      }
    </div>

    <textarea maxlength="200" placeholder="Contanos algo (opcional)"
      [value]="comment()" (input)="onComment($event)"></textarea>

    @if (store.rateProblem() === 'alreadyRated') {
      <p class="note" role="status">Ya recibimos tu calificación.</p>
    } @else if (store.rateProblem() === 'failed') {
      <p class="note note-bad" role="alert">No pudimos enviar tu calificación. Revisá la conexión y probá de nuevo.</p>
    }

    <button type="button" [disabled]="stars() === 0 || store.rating()" (click)="sendRating()">
      {{ store.rating() ? 'Enviando…' : 'Enviar' }}
    </button>
  </section>
}
```

- `[disabled]`: `stars() === 0` → no se puede enviar sin elegir · `store.rating()` → no doble envío.
- `maxlength` es **comodidad**, no seguridad: el que frena de verdad es el **dominio** (400).
- Mientras envía decí "Enviando…", nunca "Enviado": todavía puede fallar.
- Reusá las clases de estilo que ya existen (`note`, `note-bad`, `ghost`, `cta`).

### Pantalla nueva (no un bloque)

1. `features/<feature>/pages/<nombre>.page.ts|html|scss` (copiá una página parecida).
2. `features/<feature>/<feature>.routes.ts` con `loadComponent` y los guards
   (`authenticatedGuard`, `administratorGuard`...). Ejemplo: `tables.routes.ts`.
3. Entrada en `app/app.routes.ts`: `path: ':venueSlug/staff/<algo>'` + `loadChildren`.
4. Los parámetros de la URL llegan solos como `input.required<string>()`.

---

## 7. Probar

```bash
pnpm --prefix frontend run build      # does it compile?
pnpm --prefix frontend run lint
pnpm --prefix frontend run format
pnpm --prefix frontend start          # http://localhost:4200 (with the API running)
```

En el navegador, recorré el flujo real y, con **F12 → Network**, mirá el request: URL, body y status.
Después comprobá en la base que se guardó.

---

## Trampas en las que caímos

| Error | Cómo se ve | Arreglo |
|---|---|---|
| Comparar con un estado que no existe (`'AlreadyRated'`) | Error de TS "no overlap" | Mirar el `type` del store |
| Condición en el estado equivocado (`'following'`) | El formulario aparece cuando no se puede | Leer cómo cambia el estado el store |
| `$any(...)` | Rompe la regla "nada de `any`" | Método con `event: Event` + `as HTMLTextAreaElement` |
| No formatear `schema.d.ts` | 8000 líneas en el diff | `prettier --write` después de generar |
| Leer un signal sin `()` en el html | No se actualiza o muestra `[Function]` | `store.rating()` |
| `subscribe` sin `takeUntilDestroyed` | Rompe una regla del proyecto | `.pipe(takeUntilDestroyed(this.destroyRef))` |
| Archivo sin guardar | "Yo lo cambié pero no anda" | Cmd+S / File → Auto Save |

## Checklist del front

- [ ] `generate:api` + Prettier sobre `schema.d.ts`.
- [ ] Tipos de `components['schemas'][...]`, nunca a mano.
- [ ] Service: URL con `/api`, `anonymously()` si es del cliente.
- [ ] Store: signal privado + `asReadonly()`, guarda de doble envío, `takeUntilDestroyed`.
- [ ] Error nuevo en `problem-types.ts`.
- [ ] Page: `inject()`, `signal`/`computed`, sin `any`, `@if`/`@for`.
- [ ] Textos al usuario en español (de vos); todo lo demás en inglés.
- [ ] `build` + `lint` + `format` limpios. Probado en el navegador.
