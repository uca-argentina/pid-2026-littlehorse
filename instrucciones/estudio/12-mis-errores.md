# 12 · Mis errores y mis dudas

> Todo lo que me salió mal o tuve que preguntar en las consignas B, C, D, E, 2.3 y 3.6,
> **agrupado por la causa**, no por la consigna. Los errores se repiten con otra cara: si
> reconocés el patrón, lo encontrás en 2 minutos en vez de 20.
>
> La tabla rápida del front está en `11-repaso-frontend.md` §12. Esto es más amplio.

## El ranking: lo que más me pasó

| # | Patrón | Veces | Consignas |
|---|---|---|---|
| 1 | **Agregué algo y me olvidé de uno de los lugares que lo repiten** | 9 | C, D, E, 3.6 |
| 2 | **La API corría una versión vieja** (no la reinicié, o no guardé el archivo) | 2 | 3.6 |
| 3 | **Un cambio en una "forma" (record, enum, constructor) rompió a quien la usa** | 5 | C, D, E, 3.6 |
| 4 | **Copié un molde y no cambié todo lo que había que cambiar** | 6 | B, D, 3.6 |
| 5 | **Puse un "Y" donde quería un "O"** (políticas y guards) | 1 | 2.3 |
| 6 | **Puse la regla en la capa equivocada** | 4 | B, C, E |
| 7 | **Leí mal o resolví a medias el enunciado** | 4 | parcial, D, E |
| 8 | **Miré en el lugar equivocado para comprobar** | 2 | 3.6 |
| 9 | Errores de tipeo y de sintaxis | 6 | B, C |

---

## 1 · Me olvidé de uno de los lugares que lo repiten

**Lo que pasó:**

| Consigna | Agregué… | …y me olvidé de | Síntoma |
|---|---|---|---|
| D | `OnHold` al dominio | el filtro `OnTheirWay` de `KdsQueueQueries` | el pedido **desaparecía** del KDS |
| D | `OnHold` al `KdsOrderStatus` | el `switch` de `ToCustomerStatus` | **500** en el celular del cliente |
| D | la columna `OnHold` | las listas de columnas de `kds-board.page.ts` (2) y el grid del `.scss` | la columna no aparecía |
| D | el estado nuevo | `BarsForOrdering.cs:35` (la carga de la barra) | un requisito del enunciado sin hacer |
| D | el evento `OrderOnHold` | la lista del test `DomainEvents_ForEveryMove_…` | test rojo |
| E | `TransferPaymentStrategy` | el `AddScoped` en `InfrastructureServices` | "método no disponible" |
| E | `Transfer` al enum | el array `NightMetrics.Methods` | no salía la fila en el reporte |
| E | la estrategia | la lista de estrategias **del test** (`OrdersEndpointsTests:223`) | 400 en mi propio test |
| 3.6 | `MarkProductNotFavoriteHandler` | registrarlo (registré dos veces el otro) | el endpoint fallaba |

**La regla:** casi nada en este proyecto vive en un solo lugar. Hay **listas paralelas** que
tienen que tener los mismos elementos:

- **Un enum nuevo** → el enum de dominio · el de contrato · **los dos `switch`** de
  `ContractEnums` · cualquier array que liste "todos los valores" (`NightMetrics.Methods`) ·
  los `Record<…>` del front · los tests que listan todos los valores.
- **Un estado nuevo de `Order`** → además: los filtros por estado (`KdsQueueQueries`,
  `BarsForOrdering`, `CashierQueries`) · `JOURNEY`, `WHAT_IS_HAPPENING` y `CLAIMABLE` del
  seguimiento · `SAYS` y `URGENCY` de la tarjeta del menú.
- **Una clase que se inyecta** (handler, estrategia) → su `AddScoped`. **Siempre.**
- **Un evento de dominio** → el test que verifica que todos los eventos se disparan.

**Cómo lo detecto:** antes de decir "listo", busco el **vecino** de lo que agregué. Si agregué
`Transfer`, busco `Cash` en todo el repo: cada lugar donde aparece `Cash` es un lugar donde
probablemente tiene que aparecer `Transfer`.

```bash
grep -rn "VipBalance" backend/src frontend/src/app | grep -vE "/obj/|/bin/|schema.d.ts"
```

---

## 2 · La API corría una versión vieja

**Lo que pasó:**
- 3.6: arreglé el orden de `ProductListItem` a las 17:03, pero la API había arrancado a las
  16:59. El front mostraba todos destacados y la base decía que ninguno.
- 3.6: escribí el `ThenByDescending` y **no guardé** el archivo.

**La regla:** `dotnet run` compila **una vez, al arrancar**. Cada cambio en `backend/` →
Ctrl+C → `dotnet run` otra vez. El front no lo necesita: `ng serve` recarga solo.

**Cómo lo detecto:**
- ¿La pestaña del editor tiene el puntito de "sin guardar"? → Cmd+S.
- ¿Arrancó la API **después** de mi último cambio?
  ```bash
  ps -o lstart= -p $(lsof -t -iTCP:7176 -sTCP:LISTEN | head -1)
  ```
- Alternativa: `dotnet watch run --project backend/src/DrinkIt.Api` recompila sola.

---

## 3 · Cambié una "forma" y rompí a quien la usa

**Lo que pasó:**

| Consigna | Cambié | Se rompió |
|---|---|---|
| C | parámetros **obligatorios** nuevos en un record/command | los tests que lo construían con los parámetros viejos |
| D | `IsOnHold` en `KdsQueueOrderResponse` | los objetos de prueba de los specs del KDS (y el campo sobraba: ya estaba `status`) |
| E | `Method` en `CashierOrder` | `CashierEndpointsTests.cs:177` y `:241` → **ningún** test de Api compilaba |
| E | el test de métricas | esperaba `[Cash, Digital, VipBalance]` escrito a mano |
| 3.6 | `IsFavorite` en `ProductListItem` | **lo puse en otra posición** en el `new(...)` de la query: `IsActive` recibió "¿está agotado?" y todo apareció dado de baja |

**La regla:**
- Un **record posicional** recibe los valores **por posición, no por nombre**. Con varios `bool`
  seguidos, un orden equivocado **compila igual**. Contá la posición en la declaración y repetila
  en cada `new(...)`, o usá argumentos con nombre: `IsActive: product.IsActive`.
- Un **parámetro obligatorio nuevo** rompe a todos los que construyen ese tipo, incluidos los
  tests. Opciones: pasarlo en cada lugar, o darle un default (`= null`) al final.
- **No agregues un campo que ya se deduce de otro.** `IsOnHold` sobraba porque estaba `Status`.

**Cómo lo detecto:** después de cambiar un record, busco **todos sus `new`**:
```bash
grep -rn "new ProductListItem\|new(" backend/src/DrinkIt.Infrastructure/Menu/ProductQueries.cs
```
Y corro **todos** los proyectos de tests, no sólo el que toqué.

---

## 4 · Copié un molde y no cambié todo

**Lo que pasó:**
- B: los dos métodos del servicio pegaban a la misma URL (`/access-code`).
- 3.6: las URLs de `markFavorite` y `markNotFavorite` **cruzadas**: el switch "sacar" volvía a
  destacar.
- D: la primera línea de `PutOnHold` copiada de otro método: `if (Status == OrderStatus.Queued) return;`
  en vez de comparar con **su propio destino** (`OnHold`).
- D: los comentarios copiados describían otra cosa ("`Entregado` by hand", "marked ready by
  mistake"). El nombre del archivo no coincidía con la clase.
- 3.6: los comentarios de los handlers de favorito decían "US-07: turns the switch back on, e.g.
  after restocking".
- 3.6: el comentario "The template disables it too", y mi botón no tenía `[disabled]`.

**La regla:** copiar el molde está **bien**, es como se trabaja en este proyecto. Pero después
de copiar, recorré **cada línea** de lo copiado y preguntate: ¿esto dice lo que hace **mi**
versión? En especial:
- **Las URLs y los nombres de endpoint**, que son texto y el compilador no los chequea.
- **La primera línea de una transición** (idempotencia): compara con el estado **al que vas**.
- **Los comentarios**: un comentario que miente es peor que ninguno. En la defensa te lo marcan.

---

## 5 · Puse un "Y" donde quería un "O"

**Lo que pasó (2.3):**
```csharp
.RequireAuthorization(Policies.Administrator, Policies.Cashier)   // admin AND cashier → nobody
```
```ts
canActivate: [authenticatedGuard, administratorGuard, cashierGuard],   // all three → nobody
```
Y encima, al ponerlo en el **grupo**, le daba al cajero crear y editar noches.

**La regla:**

| Dónde | Significa |
|---|---|
| Varias políticas en `RequireAuthorization(a, b)` | **Y** |
| La política del grupo + la del endpoint | **Y** |
| Varios requisitos dentro de una política (`RequireRole(x).AddRequirements(y)`) | **Y** |
| `RequireRole(a, b)` dentro de **una** política | **O** |
| Varios guards en `canActivate: [...]` | **Y** |

Para un "O": **una política** con `RequireRole(a, b)` en `StaffAuthentication.cs`, y **un guard**
que reciba varios roles. Y aplicarlo sólo a los endpoints que corresponde, no al grupo entero.

El recorrido completo de una petición, en orden: `JwtTokenIssuer` (qué claims tiene el token) →
`authentication-interceptor.ts` (pega el token) → `UseAuthentication` (valida, 401) →
`VenueResolutionMiddleware` (el boliche, del claim) → `UseAuthorization` (la política, 403) →
`TonightsCrewRequirement` → `ProblemDetailsPipeline` (el `type` del 401 o del 403). El orden está
en `Program.cs:111-123`.

---

## 6 · Puse la regla en la capa equivocada

**Lo que pasó:**
- B: usé `INightTableQueries` (una **query**, sólo lectura) para **escribir**.
- C: le pasé `IUnderwayNightLookup` a `BarChooser`, que es una función **pura y estática**: no
  consulta nada, recibe todo hecho.
- E: mandé el enum **de dominio** en el Response (viajaba como número), y en el front escribí
  `if (method == 2)`. Encima el 2 era `VipBalance`, no `Cash`.
- E: no sabía dónde poner el `ToContract`.

**La regla:**

| Pregunta | Respuesta |
|---|---|
| ¿Leo o escribo? | Leo → **query** (`AsNoTracking`, `Select` a un DTO). Escribo → **repositorio** (`GetForUpdateAsync` + `SaveChangesAsync`). |
| ¿Es una invariante de **una** entidad? | **Dominio** (`Order.PutOnHold`, `EnsureItIs`). |
| ¿Mira **varios** registros? | **Handler** + una consulta (el límite de 3 destacados, las promociones que se pisan). |
| ¿Es sólo cómo se ve? | **Front**. |
| ¿Dónde se traduce dominio → contrato? | En el **`ToResponse` del endpoint** (Api). Application no conoce los enums de contrato. |
| ¿Qué enum va en un Response? | El **de contrato** (`PaymentMethodName`), que viaja como texto. Nunca el de dominio. |

---

## 7 · Leí mal o resolví a medias el enunciado

**Lo que pasó:**
- **El parcial real:** entendí que había que cancelar la noche. Avancé bastante con eso y no me
  alcanzó el tiempo para lo que se pedía.
- D: no hice el punto 4 (carga de la barra). El texto y el botón no eran los literales del
  enunciado ("Despausar" en vez de "Reanudar").
- E: no hice 2a (el alias en el seguimiento) ni 2b (la etiqueta en la caja). La caja mostraba
  `Transfer` en inglés.
- D y E: dije "cumplo la consigna" con tests rojos.

**La regla:**
1. **Los primeros 10 minutos, sin código.** Leé el enunciado dos veces y subrayá los verbos.
   Escribí una línea por punto numerado: qué capa toca y qué molde copio.
2. **Releé el caso de validación:** es la definición de "terminado".
3. **Los textos entre comillas del enunciado son literales.** Copialos tal cual.
4. Antes de decir "listo": **un tilde por cada punto numerado**, y tests verdes.

---

## 8 · Miré en el lugar equivocado para comprobar

**Lo que pasó (3.6):** "en el front se pone, pero en la tabla no veo el 1". Se estaba guardando
bien. Las causas posibles:
- Hay **dos bases** en el contenedor: `drinkit` (la de la API) y `drinkit_pr30` (otra rama).
- `Products` tiene más de 1700 filas, casi todas `E2E …` de los tests de Playwright. El visor
  muestra sólo las primeras.

**La regla:** para comprobar en la base, **una consulta, no abrir la tabla**:
```sql
SELECT Name, IsFavorite, LastModifiedAt FROM Products
WHERE IsFavorite = 1 ORDER BY LastModifiedAt DESC;
```
Y confirmá que el visor está conectado a `drinkit`.

---

## 9 · Tipeo y sintaxis

| Error | Dónde | Cómo se ve |
|---|---|---|
| `}` de más en una ruta | B | `ASP0017` al compilar |
| `∫` (Option + B en Mac) | B | `Unexpected character` |
| Una `n` suelta | C | error de compilación raro |
| `toString` sin `()` | C | muestra el texto de una función |
| `if` de dos líneas sin llaves | C | lo prohíbe `CLAUDE.md`: `if (x) return y;` en una línea |
| Métodos en PascalCase en TS (`PutOnHold`) | D | el resto del front usa camelCase |
| "pusto", "transferis" | D, E | textos que ve el usuario |

---

## Lo que pregunté (y dónde está la respuesta)

Las dudas de concepto, para repasarlas antes de la defensa:

| Pregunta | Dónde está |
|---|---|
| ¿Cómo se comunica el front con el back? ¿Dónde se hace la petición? | `11-repaso-frontend.md` (el `httpResource` y el molde de las acciones) y el artifact "El viaje del switch" |
| ¿Dónde se definen los Request y Response? ¿Por qué hay dos tipos arriba del `schema`? | Los records `…Request`/`…Response` están arriba de cada `…Endpoints.cs` (Api). `generate:api` los copia a `schema.d.ts`, y el front les pone un alias corto: `type X = components['schemas']['XResponse']` |
| ¿Cómo agrego un campo que el front no tiene (`table.isActive`)? | La cadena de 5 pasos: record → query → Response → `generate:api` → template |
| ¿Cuál es la diferencia entre query y repository? | Patrón 6 de este archivo, y `CLAUDE.md` "Persistencia" |
| ¿`SaveAsync` o `SaveCollectionAsync`? ¿Cómo descuenta stock cada pago? | `AddAsync` (digital/VIP), `SaveCollectionAsync` (efectivo en la caja), `SaveCancellationAsync` (devuelve) |
| ¿Cómo cuento los pedidos de una barra? | `GroupBy(order => order.KdsId)` sobre `Orders` filtrado por estado (`BarsForOrdering`) |
| ¿Qué comandos de EF uso para traer datos? | `Where`, `Select` a un DTO, `AsNoTracking`, `GroupBy`, `CountAsync`, `AnyAsync`, `ToDictionaryAsync`. Los ejemplos reales: `BarsForOrdering.cs` y `ProductQueries.cs` |
| ¿Cómo son las tablas y cómo se relacionan? ¿Cómo creo, deshago y borro una migración? | `13-modelo-de-datos.md` |
| ¿Qué es el "till"? | La caja: `TillHub`, `ITillNotifier`, `/hubs/till` |
| ¿Qué pasa entre que se manda un pedido y se asigna a un KDS? | `ConfirmOrderHandler` → `BarsForOrdering` → `BarChooser` → estrategia → `Enqueue` (y en efectivo, de nuevo en `CollectCashHandler`) |
| ¿Cómo se revisa la autenticación y el rol de una petición? | Token → `UseAuthentication` (401) → `VenueResolutionMiddleware` → `UseAuthorization` (403) → `ProblemDetailsPipeline` |
| ¿Cómo ordeno por un campo nuevo? | `.ThenByDescending(...)` entre los criterios existentes, y decidir qué criterio manda |
| ¿Cómo se dice "transferencia" en el código? | `BankTransfer` (y agregarlo al glosario) |

---

## La lista antes de decir "listo"

Pegala al lado del monitor el día del examen.

1. [ ] **Cada punto numerado** del enunciado tiene su tilde, y los textos son **literales**.
2. [ ] **Busqué el vecino** de cada cosa que agregué (enum, estado, handler) en todo el repo.
3. [ ] **Cada clase nueva que se inyecta** tiene su `AddScoped`.
4. [ ] **Cada `new(...)` de un record que cambié** tiene el campo **en la misma posición**.
5. [ ] **Cada `switch` de `ContractEnums`** tiene la rama nueva.
6. [ ] **Guardé todo y reinicié la API.** Si cambió un Response: `generate:api`.
7. [ ] Releí **lo que copié**: URLs, nombres de endpoint, primera línea de las transiciones,
       comentarios.
8. [ ] **Compila el back entero** (`dotnet build backend/DrinkIt.slnx`), incluidos los tests.
9. [ ] Corren **todos** los tests del back y `pnpm --prefix frontend run test:ci`.
10. [ ] `pnpm --prefix frontend run lint`.
11. [ ] Probé **el caso de validación** en la app, con el rol que corresponde. Si toca la base,
        lo comprobé con una **consulta**.
