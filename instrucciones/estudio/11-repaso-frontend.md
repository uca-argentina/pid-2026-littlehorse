# Estudio 11 — Repaso del frontend: las piezas que se repiten en todas las pantallas

> Todo el front de drink.it está hecho con las mismas ~15 piezas. El ejemplo de referencia es
> **Mesas** (`features/tables/`): es la feature más chica y tiene de todo. Cada bloque de código
> está copiado del proyecto.

---

## 1. Cómo está armada una feature

```
features/tables/
├── tables.routes.ts          ← qué URL abre qué pantalla (y los guards)
├── tables.service.ts         ← las llamadas HTTP de acciones + los tipos (apodos del schema)
├── new-table.store.ts        ← el estado de un formulario (enviando, error, …)
├── table-fields.ts           ← validadores y mensajes compartidos entre "nueva" y "editar"
└── pages/
    ├── tables.page.ts        ← la lista: lógica
    ├── tables.page.html      ← la lista: lo que se dibuja
    ├── tables.page.scss      ← la lista: estilos
    ├── tables.page.spec.ts   ← la lista: tests
    ├── new-table.page.*      ← el formulario de alta
    └── edit-table.page.*     ← el formulario de edición
```

**Regla:** una pantalla = cuatro archivos con el mismo nombre (`.ts`, `.html`, `.scss`,
`.spec.ts`). El `.ts` dice *qué hay*, el `.html` dice *cómo se ve*.

---

## 2. Anatomía de un componente (`tables.page.ts`)

```ts
@Component({
  selector: 'drinkit-tables-page',          // ① el nombre de la etiqueta: <drinkit-tables-page />
  imports: [AdminHeader, RouterLink],       // ② lo que usa el HTML (otros componentes, directivas)
  styleUrl: './tables.page.scss',
  templateUrl: './tables.page.html',
})
export class TablesPage {
  readonly venueSlug = input.required<string>();          // ③ dato que viene de AFUERA (acá, de la URL)

  private readonly service = inject(TablesService);       // ④ pedir un servicio (en vez del constructor)
  private readonly destroyRef = inject(DestroyRef);

  protected readonly tables = httpResource<VipTable[]>(() => TABLES_URL);   // ⑤ GET automático

  protected readonly rows = computed<VipTable[]>(() =>                       // ⑥ dato DERIVADO
    this.tables.hasValue() ? this.tables.value() : [],
  );

  protected readonly changing = signal(false);                               // ⑦ estado PROPIO

  protected putAway(table: VipTable): void {                                 // ⑧ lo que llama un (click)
    this.change(this.service.deactivate(table.id));
  }
}
```

| # | Pieza | Para qué | Cómo se lee en el HTML |
|---|---|---|---|
| ① | `selector` | El nombre con que otro HTML lo usa | `<drinkit-admin-header />` |
| ② | `imports` | Si el HTML usa un componente o `routerLink`, va acá. Si falta: error de compilación | — |
| ③ | `input()` | Dato que manda el padre (o el router, por `:venueSlug`) | `venueSlug()` |
| ④ | `inject()` | Traer un servicio. Nunca por constructor (convención del proyecto) | — |
| ⑤ | `httpResource` | Leer del back al abrir la pantalla | `tables.value()`, `tables.isLoading()` |
| ⑥ | `computed()` | Algo que se calcula de otros signals | `rows()` |
| ⑦ | `signal()` | Estado que cambia el propio componente | `changing()` |
| ⑧ | método | Una acción del usuario | `(click)="putAway(table)"` |

**`private` / `protected` / `readonly`:**
- `protected` = lo usa el HTML. `private` = sólo lo usa el `.ts`. Si el HTML lee algo `private`,
  no compila.
- `readonly` en todo: el *signal* no se reemplaza, se le cambia el valor con `.set()`.

**Lo que NO se escribe** (CLAUDE.md): `standalone: true` y `ChangeDetectionStrategy.OnPush`.
Ya son el default.

---

## 3. El HTML: la sintaxis que vas a leer siempre

| Escribís | Significa | Ejemplo real |
|---|---|---|
| `{{ x }}` | Mostrar un texto | `Mesa {{ table.number }}` |
| `[prop]="x"` | Pasarle un dato a una propiedad o a un hijo | `[venueSlug]="venueSlug()"` |
| `(evento)="metodo()"` | Cuando pasa algo, llamá a esto | `(click)="putAway(table)"` |
| `[class.nombre]="cond"` | Poner una clase CSS si se cumple | `[class.row-off]="!table.isActive"` |
| `[attr.nombre]="x"` | Poner un atributo HTML (casi siempre accesibilidad) | `[attr.aria-label]="'Editar la mesa ' + table.number"` |
| `[disabled]="cond"` | Deshabilitar un botón | `[disabled]="changing()"` |
| `@if (cond) { } @else { }` | Mostrar sólo si… | `@if (tables.isLoading()) { Cargando… }` |
| `@for (x of lista(); track x.id) { }` | Repetir por cada elemento. `track` es obligatorio | `@for (table of rows(); track table.id)` |
| `@switch (x) { @case (v) { } }` | Elegir entre varios | El error del checkout |
| `#nombre` | Referencia a un elemento, para usarlo en el mismo HTML | `<input #units />` |
| `$event` | Lo que trae el evento (en un input: lo tipeado) | `(input)="typed('name', $event)"` |

**Los signals se leen con `()`** en el HTML: `rows()`, `changing()`, `venueSlug()`. Sin paréntesis
estás mostrando la función, no su valor.

**Textos que ve el cliente: en español. Todo lo demás (ids, clases, nombres): en inglés.**

---

## 4. El estado: qué usar y cuándo

| Herramienta | Quién lo cambia | Cuándo usarlo | Ejemplo |
|---|---|---|---|
| `signal(v)` | El componente, con `.set()` / `.update()` | Estado propio: "enviando", "qué mesa está confirmando", lo que cambié en esta página | `changing`, `switched`, `renewed` |
| `computed(() => …)` | Nadie: se recalcula solo | Todo lo que se **deriva**: filtrar, contar, armar textos | `rows`, `isEmpty`, `cards` |
| `input()` / `input.required()` | **El padre** (o el router) | Datos que vienen de afuera | `venueSlug`, `tables`, `initial` |
| `output()` | El hijo **avisa** con `.emit()` | El hijo le manda algo al padre | `saved = output<NewNight>()` en `night-form` |
| `linkedSignal(() => …)` | Arranca del `input`, después lo cambia el usuario | Un campo de formulario que arranca con el valor guardado | `name`, `maxOrderPerKds` en `night-form` |
| `effect(() => …)` | — | Hacer algo **afuera** cuando cambia un signal (navegar, guardar en localStorage). Poco: casi siempre alcanza con `computed` | `table-entry.page`: si abrió la mesa, ir a la carta |
| `toSignal(observable)` | — | Convertir algo de RxJS (un form reactivo) en signal | `typed` en `new-table.page` |

**El error que ya te pasó:** el hijo **no puede cambiar** su `input`. Si necesitás que se vea un
cambio, guardalo en un `signal` propio y combinalo en el `computed`:

```ts
isActive: this.switched().get(table.tableId) ?? table.isActive,   // lo mío gana sobre lo del padre
```

O que el hijo haga `output().emit(...)` y el **padre** haga `reload()`.

---

## 5. Hablar con el back: dos formas

### Leer al abrir la pantalla: `httpResource`

```ts
protected readonly tables = httpResource<VipTable[]>(() => TABLES_URL);
```

La petición sale **sola**. Si la URL usa un signal (`() => \`${NIGHTS_URL}/${this.id()}\``), se
vuelve a pedir cuando ese signal cambia.

| Lo que tiene | Qué es |
|---|---|
| `tables.isLoading()` | `true` mientras espera |
| `tables.hasValue()` | `true` si llegó bien |
| `tables.value()` | Lo que llegó. **Leerlo con error tira excepción:** siempre `hasValue()` antes |
| `tables.error()` | El error, o `undefined` |
| `tables.reload()` | Volver a pedir |
| `tables.value.update(lista => …)` | Cambiar lo que tiene en memoria **sin volver a pedir** |

### Hacer algo al tocar un botón: servicio + `subscribe`

```ts
// tables.service.ts — arma la petición (todavía no sale)
deactivate(id: string): Observable<VipTable> {
  return this.http.post<VipTable>(`${TABLES_URL}/${id}/deactivate`, {});
}

// tables.page.ts — la dispara y maneja la respuesta
private change(request: ReturnType<TablesService['deactivate']>): void {
  if (this.changing()) return;                       // 1. no mandar dos veces (doble tap)
  this.changing.set(true);
  this.changeFailure.set('none');

  request.pipe(takeUntilDestroyed(this.destroyRef))  // 2. si cierran la pantalla, se corta
    .subscribe({                                     // 3. ACÁ sale la petición
      next: (updated) => {                           // 4. salió bien
        this.changing.set(false);
        this.tables.value.update((tables) => tables?.map((t) => (t.id === updated.id ? updated : t)));
      },
      error: (error: unknown) => {                   // 5. salió mal
        this.changing.set(false);
        this.changeFailure.set(failureOf(error));
      },
    });
}
```

Este bloque es **el molde de toda acción** del front. Las cinco partes siempre están:
guarda contra doble tap → `takeUntilDestroyed` → `subscribe` → `next` → `error`.
(`subscribe` sin `takeUntilDestroyed` está prohibido por el CLAUDE.md.)

---

## 6. Los errores: siempre el mismo camino

```ts
type Failure = 'none' | 'forbidden' | 'inANight' | 'unreachable';     // ① los casos posibles, como texto

function failureOf(error: unknown): Failure {                          // ② error → caso
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';     //    sin respuesta: sin señal
  const type = problemTypeOf(error);                                   //    el "type" del Problem Details
  if (type === ProblemTypes.forbidden) return 'forbidden';
  if (type === ProblemTypes.tableInANightNotOver) return 'inANight';
  return 'unreachable';                                                //    cualquier otro
}
```
```html
@else if (changeFailure() === 'inANight') {                            <!-- ③ un mensaje por caso -->
  <p class="note note-bad" role="alert">Esta mesa está vendida en una noche que todavía no terminó…</p>
}
```

- `ProblemTypes` (`core/api/problem-types.ts`) es la lista **escrita a mano** de los `type` del
  back. Un error nuevo del back = una línea acá.
- **¿Por qué un tipo de texto y no varios `boolean`?** Porque así no puede estar "enviando" y
  "número tomado" a la vez. Un solo valor, un solo estado.

---

## 7. El store: el estado de un formulario aparte

Cuando una pantalla tiene **envío + varios resultados posibles**, la lógica va a un store:

```ts
type NewTableStatus = 'idle' | 'sending' | 'numberTaken' | 'unreachable';

@Injectable()                                        // ← SIN providedIn: 'root'
export class NewTableStore {
  private readonly state = signal<NewTableStatus>('idle');
  readonly status = this.state.asReadonly();         // la página lee, no escribe
  readonly isSending = computed(() => this.state() === 'sending');

  submit(venueSlug: string, table: NewVipTable): void {
    if (this.isSending()) return;
    this.state.set('sending');
    this.tables.create(table).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.showTheListing(venueSlug),    // navega a la lista
      error: (error: unknown) => this.state.set(reasonFor(error)),
    });
  }
}
```
```ts
// new-table.page.ts
@Component({ providers: [NewTableStore], … })       // ← el store vive y muere con la página
export class NewTablePage {
  protected readonly store = inject(NewTableStore);
}
```

**¿Por qué no `providedIn: 'root'`?** `root` es uno para toda la app y vive para siempre. Si
fallara un envío, al volver a entrar a la pantalla seguiría el error viejo. En `providers` del
componente, cada visita arranca limpia.

| | `providedIn: 'root'` | `providers: [X]` en el componente |
|---|---|---|
| Cuántos hay | Uno para toda la app | Uno por pantalla abierta |
| Vive | Siempre | Mientras la pantalla está abierta |
| Para qué | Servicios sin estado de pantalla: `TablesService`, `Cart`, `SessionStorage` | Stores de un formulario: `NewTableStore`, `SaveNightStore` |

---

## 8. Formularios: hay dos estilos en el proyecto

**Reactive forms** (`new-table.page`): validación declarada.
```ts
protected readonly form = new FormGroup({
  number: new FormControl('', { nonNullable: true, validators: [wholeBetweenOneAnd(TABLE_NUMBER_MAX)] }),
});
```
```html
<form [formGroup]="form" (ngSubmit)="submit()">
  <input id="number" formControlName="number" … />
```
Necesita `ReactiveFormsModule` en `imports`.

**Signals a mano** (`night-form`): cada campo es un `linkedSignal` y se actualiza en `(input)`.
```ts
protected readonly name = linkedSignal(() => this.initial()?.name ?? '');
protected typed(field: 'name' | …, event: Event): void {
  this[field].set((event.target as HTMLInputElement).value);   // ← un input SIEMPRE da texto
}
```
```html
<input [value]="name()" (input)="typed('name', $event)" />    <!-- [value] para que muestre lo guardado -->
```

En los dos, lo que se manda al back se arma en el `submit()`. Lo que no está en ese objeto
**no viaja** (te pasó con `maxOrderPerKds`).

---

## 9. Rutas y guards

```ts
// app.routes.ts — la URL general, carga la feature recién cuando hace falta
{
  path: ':venueSlug/staff/tables',
  canActivate: [rememberVenueGuard],
  loadChildren: () => import('./features/tables/tables.routes').then((m) => m.tablesRoutes),
},

// tables.routes.ts — las pantallas de la feature
{
  path: '',
  canActivate: [authenticatedGuard, administratorGuard],       // ① ¿hay sesión? ② ¿es admin?
  children: [
    { path: '', loadComponent: () => import('./pages/tables.page').then((m) => m.TablesPage) },
    { path: 'new', loadComponent: () => import('./pages/new-table.page').then((m) => m.NewTablePage) },
    { path: ':id', loadComponent: () => import('./pages/edit-table.page').then((m) => m.EditTablePage) },  // después de 'new'
  ],
},
```

- **`:venueSlug` y `:id` llegan solos como `input()`** a la página, porque `app.config.ts` usa
  `withComponentInputBinding()`. Por eso `readonly venueSlug = input.required<string>()`.
- **`'new'` va antes de `':id'`**: si no, `/tables/new` se leería como "la mesa con id `new`".
- **Un guard** es una función que devuelve `true` (pasá) o una URL (andá allá):
  ```ts
  export const administratorGuard: CanActivateFn = (route) => {
    if (sessions.session()?.role === 'Administrator') return true;
    return router.createUrlTree([venueSlug ?? '', 'staff']);
  };
  ```
  El guard **sólo esconde la pantalla**. La seguridad de verdad es el back, que responde 403.

---

## 10. Padre e hijo: el ida y vuelta

```
night.page.ts (PADRE)                                  night-form.ts (HIJO)
  nightTables = httpResource(...)
                                                        readonly initial = input<Night | null>(null);
  <drinkit-night-form                                   readonly saved = output<NewNight>();
     [initial]="night.value()"        ─── input ───►
     (saved)="save($event)"           ◄── output ───    this.saved.emit({ name, startsAt, … });
  />
  save(night) { this.store.update(...) }
```

- `[x]="…"` en la etiqueta del hijo = el padre le **manda** un dato al `input` del hijo.
- `(x)="…"` en la etiqueta del hijo = el padre **escucha** el `output` del hijo. `$event` es lo
  que el hijo mandó en `.emit(...)`.
- El hijo no sabe nada del back: avisa y el padre decide qué hacer.

---

## 11. Convenciones que se ven en todas las pantallas

- **Accesibilidad**: `role="alert"` para errores, `role="status"` para "cargando", `aria-label`
  en español en todo botón que no tenga texto claro. Los tests buscan los botones **por ese texto**.
- **Un `id` fijo y su `<label for="…">`** en cada input.
- **Clases de estado**: `note-bad`, `chip-off`, `row-off`, `input-bad`.
- **Colores y tamaños con tokens**: `var(--dk-accent)`, `var(--dk-tap-min)`, `var(--dk-space-2)`. Nunca un color suelto.
- **`slowLoading`** para el "Está tardando más de lo normal".
- **Los tipos salen del schema generado** (`components['schemas'][…]`), nunca escritos a mano.

---

## 12. Los errores que ya te pasaron (para no repetirlos)

| Síntoma | Causa | Arreglo |
|---|---|---|
| El botón no hace nada | Falta el `subscribe` o el `(click)` | Mirá Network: ¿salió la petición? |
| Se guardó pero la pantalla no cambia | Recargaste un resource que no es el que dibuja, o el dato viene por `input` | `signal` propio + `computed`, o `output` y `reload()` en el padre |
| `Property 'x' does not exist on type …` | El back no lo manda, o no regeneraste | `…Response` en C# → reiniciar API → `generate:api` |
| El aviso aparece cuando no debe | Condición al revés (`!isActive()`) | Nombrá el computed por lo que es verdad: `isSuspended` |
| `toString` muestra una función | Faltan los `()` | `toString()` |
| `Unexpected character [∫]` | Option + B en Mac | Borrar el carácter |
| El campo aparece vacío al editar | Falta `[value]="campo()"` | Agregarlo |
| El dato no llega al back | No está en el objeto del `submit()` / `emit()` | Agregarlo ahí |
| `value()` tira excepción | Leíste el valor de un resource con error | `hasValue()` antes |

---

## 13. Ejercicios (20 min)

1. Sin mirar: escribí las 8 piezas de un componente (sección 2) y qué hace cada una.
2. ¿Qué diferencia hay entre `[disabled]="x"` y `(click)="x()"`?
3. Tenés que mostrar "3 mesas dadas de baja" arriba de la lista. ¿`signal`, `computed` o `input`? Escribilo.
4. ¿Por qué `NewTableStore` está en `providers` de la página y `TablesService` es `providedIn: 'root'`?
5. Escribí el molde completo de una acción (sección 5) para un botón "Duplicar mesa" que llama a `service.duplicate(id)`.
6. Para el "7 de 8" del KDS: ¿lo traés con `httpResource` o con un servicio? ¿Y cuando el hub avisa que cambió, qué método llamás?
