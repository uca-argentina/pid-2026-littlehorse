# Guía: aplicar un cambio en el backend

Ejemplo que recorre toda la guía: **"El cliente califica su pedido entregado (1 a 5 estrellas +
comentario opcional, una sola vez). El admin ve el promedio en el reporte de la noche."**

---

## 0. Antes de escribir una línea (10 minutos)

### Las 5 preguntas

| # | Pregunta | Ejemplo (calificación) |
|---|---|---|
| 1 | **¿Qué regla de negocio nueva hay?** → va en **Domain** | "sólo un pedido entregado", "1 a 5", "una sola vez" |
| 2 | **¿Error esperable o invariante rota?** | 7 estrellas = invariante (excepción) · pedido inexistente = esperable (`Result`) |
| 3 | **¿Quién lo hace y de dónde sale el venue?** | Cliente → ruta con `{venueSlug}` + token del pedido |
| 4 | **¿Escritura o lectura?** | Calificar = escritura (repositorio) · promedio = lectura (query) |
| 5 | **¿Cambia la base?** | Sí: dos columnas en `Orders` → migración |

### Elegí el molde (la feature ya hecha que más se parece)

| Si la consigna es... | Copiá de... |
|---|---|
| El **cliente** hace algo sobre **su** pedido | Cancelar pedido: `CancelOrderHandler` + `OrderTrackingEndpoint` |
| El **personal** mueve un pedido | KDS: `KdsOrderMove` + `KdsEndpoints` |
| Un **campo nuevo** en una entidad | `Table` (la más simple) o `Product` |
| Una **entidad nueva** con ABM (alta/baja/modificación) | `Promotion` completa, o `Table` |
| Algo que pasa **al confirmar el pedido** | `ConfirmOrderHandler` (promociones, sellos) |
| Un **dato nuevo en un reporte** | `NightMetrics` + `NightMetricsQueries` |
| Un **medio de pago nuevo** | `IPaymentStrategy` (Strategy) |

### ¿Excepción o Result? (te lo van a preguntar)

> **¿Un usuario normal, usando bien la app, puede provocar esto?**
> - **No** (dato imposible, transición ilegal) → `DomainException` en **Domain** → 400 automático.
> - **Sí** (no existe, ya está tomado, sin stock) → `Result` con `Error` en **Application** → el endpoint
>   elige 404/409.

---

## 1. Domain — la regla

**Archivo:** la entidad (`backend/src/DrinkIt.Domain/<Area>/<Entidad>.cs`).

Qué agregar:

1. **Códigos de error** en `ErrorCodes`. Formato: `"<entidad>.<cosa>_<problema>"`.
   Primero *qué cosa*, después *qué le pasa*: `RatingOutOfRange`, `RatingCommentLength`, `AlreadyRated`.
2. **Constantes** de largo/rango (`public const int RatingCommentMaxLength = 200;`), al lado de las
   otras. Las vas a reusar en la configuración de EF.
3. **Propiedades** con `{ get; private set; }`. **Nullable (`int?`, `string?`)** si "todavía no pasó"
   (las filas viejas quedan en `null`).
4. **El método**, con verbo en PascalCase. Orden interno:

```csharp
public void Rate(int stars, string? comment)
{
    EnsureItIs(OrderStatus.Delivered);                       // 1. is it in the right state?

    if (Rating is not null) throw new DomainException(...);  // 2. was it already done?

    if (stars is < 1 or > 5) throw new DomainException(...); // 3. are the data valid?

    string? clean = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

    if (clean?.Length > RatingCommentMaxLength) throw new DomainException(...);

    Rating = stars;                                          // 4. assign LAST
    RatingComment = clean;
}
```

**Validar todo y asignar al final**: si asignás y después falla una validación, el objeto queda a
medio modificar.

### Trampas del Domain

- ❌ **No inventes un estado nuevo** si lo que agregás es un *dato sobre* el pedido. `Rated` como
  estado rompía `IsFinished()`, métricas y seguimiento. Los estados de `Order` describen dónde está el
  trago. "Ya calificado" = `Rating is not null`.
- ❌ Ningún `using` de EF Core ni de ASP.NET en Domain (lo verifican los tests de arquitectura).
- ❌ Ningún setter público.
- ⚠️ Un `/// <summary>` documenta lo que está **justo debajo**. No metas código entre un comentario y
  lo suyo.

Compilá: `dotnet build backend/src/DrinkIt.Domain`

---

## 2. Infrastructure — la base

### 2a. Configuración de EF

**Archivo:** `backend/src/DrinkIt.Infrastructure/Persistence/Configurations/<Entidad>Configuration.cs`

```csharp
builder.Property(order => order.Rating);
builder.Property(order => order.RatingComment).HasMaxLength(Order.RatingCommentMaxLength);
```

- **Todo `string` lleva `HasMaxLength`** (si no, `nvarchar(max)`).
- La nulabilidad sale sola del tipo (`int?` → `NULL`). `IsRequired()` sólo si no puede ser null.
- Usá **la constante del dominio**, nunca el número suelto: así dominio y base no se desincronizan.
- ¿Por qué un archivo aparte y no `[MaxLength]` en la entidad? Porque Domain no puede depender de EF.

**Entidad nueva**, además:
- `DbSet<X>` en `DrinkItDbContext` + `HasQueryFilter(x => x.VenueId == CurrentVenueId)`.
- La entidad implementa `IBelongsToVenue` y hereda de `AuditStamps` → `builder.HasAuditColumns()`.
- Índices únicos **compuestos con `VenueId`**: `HasIndex(x => new { x.VenueId, x.Number }).IsUnique()`.
- Repositorio/queries nuevos → registrarlos en `InfrastructureServices.cs` (`AddScoped<IX, X>()`).

### 2b. Migración

⚠️ **Terminá los nombres de las propiedades ANTES.** El nombre de la columna sale del de la propiedad.

```bash
dotnet ef migrations add AddOrderRating \
  --project backend/src/DrinkIt.Infrastructure \
  --startup-project backend/src/DrinkIt.Api \
  --output-dir Persistence/Migrations
```

**Abrí el `<fecha>_AddOrderRating.cs` y verificá** que el `Up()` tenga sólo lo que esperás (acá: dos
`AddColumn` en `Orders`, nullable) y el `Down()` lo inverso. Si aparece otra tabla, frená.

Se aplica sola al levantar la API en Development.

---

## 3. Application — el caso de uso

### Escritura (comando)

**Archivo nuevo:** `backend/src/DrinkIt.Application/<Area>/<Accion>Handler.cs`

```csharp
using DrinkIt.Application.Common;   // ← Result, Error (si falta: error CS0246)
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

public sealed record RateOrderCommand(string Code, string Token, int Stars, string? Comment);

public sealed class RateOrderHandler(IOrderRepository orders)
{
    public async Task<Result<OrderStatus>> HandleAsync(RateOrderCommand command, CancellationToken cancellationToken)
    {
        if (!OrderCode.TryParse(command.Code, out OrderCode? parsed)) return OrderErrors.NotFound;

        Order? order = await orders.GetForUpdateAsync(parsed!, cancellationToken);

        if (order is null || !order.TrackingToken.Matches(command.Token)) return OrderErrors.NotFound;

        order.Rate(command.Stars, command.Comment);   // DomainException is NOT caught: it goes up to the global handler

        Result<Order> saved = await orders.SaveAsync(order, cancellationToken);

        return saved.IsSuccess ? order.Status : saved.Error!;
    }
}
```

- **Command (`record`)** cuando hay más de 2 datos. Vive en Application.
- **Mismo error para "no existe" y "token incorrecto"** → no le das pistas a un atacante.
- **Reusá los métodos del repositorio que ya existen.** Agregar un método a una interfaz rompe los
  *fakes* de los tests.
- **Leé qué hace cada método que copiás.** `SaveCancellationAsync` devuelve stock: usarlo para calificar
  sumaba tragos al stock. El genérico es `SaveAsync`.
- **No copies comentarios del molde sin adaptarlos.** Un comentario que miente es peor que ninguno.
- Errores esperables nuevos → `static readonly Error` en `<Area>Errors.cs`.

### Lectura (query / reporte)

No hay repositorio: **query con `AsNoTracking()` que proyecta a un record**, y el cálculo en
Application. El dato nuevo tiene que **atravesar cada record intermedio**:

```
Columna en la DB
  → record de la query        (Application · INightMetricsQueries.cs → PaidOrder)   + "int? Rating = null" AL FINAL
  → proyección .Select(...)   (Infrastructure · NightMetricsQueries.cs)              + order.Rating
  → modelo calculado          (Application · NightMetrics.cs → Build)                + orders.Select(o => o.Rating).Average()
  → Response del endpoint     (Api · NightsEndpoints.cs → NightMetricsResponse + Of) + metrics.AverageRating
```

Si te salteás uno, el dato se pierde en el camino.

- **Parámetros nuevos en records existentes: al final y con valor por defecto** → los tests viejos
  siguen compilando. (En el *Response* conviene **sin** default: así el compilador te obliga a pasarlo
  en `Of(...)`.)
- **LINQ:** `Where` **filtra** (la lambda devuelve `bool`) · `Select` **transforma**.
  `Average()` sobre `int?` ignora los `null` y devuelve `null` si no hay ninguno (no divide por cero).

---

## 4. Api — el endpoint

**Archivo:** `backend/src/DrinkIt.Api/Features/<Area>/<Area>Endpoints.cs`

### 4a. El record del body (lo que tipea el cliente, nada más)

```csharp
public sealed record RateOrderRequest(int Stars, string? Comment);
```

⚠️ **Nunca recibas el `Command` como body.** El endpoint arma el command juntando URL + body. Si no, la
URL miente y el contrato público queda atado a un tipo interno.

### 4b. El mapeo

```csharp
endpoints
    .MapPost("/{venueSlug}/orders/{code}/{token}/rate", RateAsync)
    .AllowAnonymous()                          // client: no login
    .WithMetadata(new ScopedBySlugAttribute()) // venue from the URL slug
    .WithName("RateMyOrder")
    .WithTags("Orders")
    .WithSummary("Rates a delivered order, for the customer holding its link.")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status400BadRequest)   // DomainException
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);
```

| Quién llama | Ruta | Autorización | Venue sale de |
|---|---|---|---|
| **Cliente** | `/{venueSlug}/...` | `.AllowAnonymous()` + `ScopedBySlugAttribute` | el slug de la URL |
| **Personal** | `/staff/...` (**sin slug**) | `.RequireAuthorization(Policies.X)` | el claim del JWT |

- `POST` (o `PUT`/`DELETE`) para lo que **cambia** datos. `GET` sólo lee.
- Las acciones van con verbo, como el resto: `cancel`, `mark-ready`, `rate`.
- `.Produces*` es **documentación** (OpenAPI → Scalar → `schema.d.ts`). Lo que el cliente recibe es lo que
  devuelve tu código.

### 4c. El método

```csharp
internal static async Task<IResult> RateAsync(
    string venueSlug, string code, string token,   // ← from the URL
    RateOrderRequest request,                      // ← from the JSON body
    CurrentVenue venue, RateOrderHandler handler,  // ← from DI
    CancellationToken cancellationToken)
{
    if (venue.Identity is null) return NoSuchOrder();

    Result<OrderStatus> result = await handler.HandleAsync(new RateOrderCommand(code, token, request.Stars, request.Comment), cancellationToken);

    if (result.IsSuccess) return TypedResults.NoContent();

    if (result.Error!.Code == OrderErrors.NotFound.Code) return NoSuchOrder();

    return TypedResults.Problem(title: "Conflict", detail: result.Error.Message,
        statusCode: StatusCodes.Status409Conflict, type: ProblemTypes.For(result.Error.Code));
}
```

**Cero lógica de negocio acá**: valida, delega, traduce.

| Resultado | Lo resuelve | Status |
|---|---|---|
| Éxito | vos | 204 / 200 / 201 |
| `Result` con NotFound | vos | 404 |
| Otro `Result` con error | vos | 409 (o 400) |
| `DomainException` | `GlobalExceptionHandler`, solo | 400 |

### 4d. Registrar el handler (el compilador NO lo detecta)

`backend/src/DrinkIt.Api/Features/<Area>/<Area>Handlers.cs`:

```csharp
services
    .AddScoped<ConfirmOrderHandler>()
    .AddScoped<CancelOrderHandler>()
    .AddScoped<RateOrderHandler>();   // ← the ';' only on the LAST one in the chain
```

Sin esto, ASP.NET cree que el handler viene del body y **la API se cae al arrancar**.
Área nueva: crear `AddXxxHandlers()` y llamarlo en `Program.cs`, más `app.MapXxx()`.

---

## 5. Probar

```bash
dotnet build backend/DrinkIt.slnx            # everything, tests included
dotnet run --project backend/src/DrinkIt.Api # does it start? read the log
```

1. **Scalar:** `https://localhost:7176/scalar` → buscá el endpoint → ¿el body pide lo que esperás?
2. **Datos falsos** → ¿404?
3. **Datos reales** (un pedido de la base, ver `comandos-utiles.md`) → ¿204?
4. **Repetir / datos inválidos** → ¿el error esperado? Pensá en qué orden corre el handler: con un
   código inexistente y 9 estrellas da **404**, porque `Rate()` nunca llega a ejecutarse.
5. **La base:** ¿se guardó? ¿El estado **no** cambió?

---

## Checklist del backend

- [ ] Domain: regla + códigos + constantes + `private set` + validar antes de asignar.
- [ ] Sin estados nuevos inventados; sin `using` de EF en Domain.
- [ ] Configuración EF con `HasMaxLength(Entidad.Constante)`.
- [ ] Migración generada **después** de los nombres finales, y revisada.
- [ ] Handler: `Result`, sin `try/catch` de `DomainException`, `using DrinkIt.Application.Common`.
- [ ] Endpoint: `Request` aparte del `Command`, cero lógica, status correctos, `.Produces*` completos.
- [ ] Handler registrado en `XxxHandlers.cs`.
- [ ] Records existentes: parámetros nuevos al final y con default.
- [ ] `dotnet build backend/DrinkIt.slnx` + `dotnet format backend/DrinkIt.slnx` +
      `dotnet test backend/tests/DrinkIt.ArchitectureTests`.
- [ ] Todo en inglés. Archivos guardados (Cmd+S).
