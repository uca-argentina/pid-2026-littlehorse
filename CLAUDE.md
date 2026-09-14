# drink.it

PWA de pedidos de tragos para boliches. El cliente pide y paga desde su celular, el
bartender prepara con un KDS en tablet, y el retiro es asíncrono (push + estado en
tiempo real). Diseño funcional completo en `docs/drink.it.v2.md` y `docs/flujo-pedido.md`.
**Leé esos dos archivos antes de tocar código de dominio.** Si hay un sprint en curso,
leé también su consigna en `docs/sprints/` — define el alcance del sprint.

> `docs/historico/drink.it.v1.md` está **obsoleto**: describe avisos por WhatsApp, que v2
> reemplazó por push desde la PWA. Nunca lo uses como referencia para implementar.

## Stack

| Capa | Tecnología |
|---|---|
| Backend | .NET 10 · Minimal APIs · EF Core · SignalR |
| Frontend | Angular 22 (standalone + signals) · PWA (`@angular/service-worker`) |
| Base de datos | Azure SQL (SQL Server local vía Docker para dev) |
| Hosting | Azure Container Apps (API) + Static Web Apps (PWA) — ver ADR-0007 |
| CI/CD | GitHub Actions · OIDC hacia Azure (sin secretos de larga vida) |

## Estructura del monorepo

```
backend/
  src/
    DrinkIt.Domain/          # Entidades, value objects, reglas. CERO dependencias externas.
    DrinkIt.Application/     # Casos de uso (handlers), puertos (interfaces), DTOs.
    DrinkIt.Infrastructure/  # EF Core, pasarela de pago, Web Push, SignalR, impresora.
    DrinkIt.Api/             # Minimal API endpoints, DI, middleware. Capa fina.
  tests/
    DrinkIt.Domain.Tests/         # Unitarios puros, sin mocks, rapidísimos.
    DrinkIt.Application.Tests/    # Unitarios con dobles de prueba de los puertos.
    DrinkIt.Infrastructure.Tests/ # Lógica de infraestructura sin servicios externos.
    DrinkIt.Api.IntegrationTests/ # WebApplicationFactory + Testcontainers.
    DrinkIt.ArchitectureTests/    # Reflexion sobre los ensamblados; las reglas de abajo son ejecutables.
frontend/
  src/app/
    core/       # Servicios singleton, interceptors, guards.
    shared/     # Componentes tontos reutilizables, pipes, directivas.
    features/   # Una carpeta por feature vertical (menu, checkout, estado-pedido, kds, caja, mozo).
  e2e/          # Playwright.
infra/          # Bicep.
docs/
  drink.it.v2.md          # Diseño funcional VIGENTE. Fuente única de verdad.
  flujo-pedido.md         # Diagrama de secuencia del flujo completo.
  adr/                    # Architecture Decision Records.
  sprints/                # Consignas de la cátedra, una por sprint.
  historico/              # OBSOLETO — NO leerlo para escribir código (ver aviso abajo).
```

## Comandos

```bash
# Base de datos local — ANTES de correr la API
docker compose up -d                    # SQL Server en localhost,1433
docker compose down                     # apagarla; el volumen conserva los datos

# Backend
dotnet test backend/DrinkIt.slnx                    # todos los tests
dotnet test backend/tests/DrinkIt.Domain.Tests     # loop rápido de TDD
dotnet format backend/DrinkIt.slnx --verify-no-changes
dotnet run --project backend/src/DrinkIt.Api        # necesita la base levantada

# Frontend — pnpm, no npm: la versión sale de "packageManager" en package.json
pnpm --prefix frontend test             # Vitest en watch, loop rápido de TDD
pnpm --prefix frontend run test:ci      # una sola pasada, lo que corre la CI
pnpm --prefix frontend run lint
pnpm --prefix frontend run format:check
pnpm --prefix frontend start

# Punta a punta — Playwright. Necesita la base levantada y nada más: la API y el
# ng serve los arranca la prueba, y reutiliza los que ya estén corriendo.
pnpm --prefix frontend run e2e          # sin ventana, lo que se corre normalmente
pnpm --prefix frontend run e2e:headed   # viendo el navegador
pnpm --prefix frontend run e2e:ui       # modo interactivo, para depurar un test
```

**La API no arranca sin la base.** La cadena de conexión apunta a `localhost,1433` con las
credenciales del `docker-compose.yml`. En Development se aplica las migraciones y siembra el
boliche y el administrador sola: no hay que correr `dotnet ef database update` a mano.

Los tests son otra cosa y **no** usan ese contenedor: los de integración levantan el suyo con
Testcontainers y lo tiran al terminar, así que sólo necesitan Docker corriendo.

Los de punta a punta sí usan la base local: manejan un navegador contra la API y los datos
que siembra Development. La primera vez hay que bajar el navegador con
`pnpm --prefix frontend run e2e:install`. Todavía no corren en la CI.

## Regla de dependencias (verificada por `DrinkIt.ArchitectureTests`)

```
Api  →  Application  →  Domain
             ↑
      Infrastructure  (implementa los puertos de Application; nadie la referencia salvo el
                       Composition Root en Api/Program.cs)
```

- `Domain` no referencia **nada** (ni EF Core, ni ASP.NET, ni System.Text.Json).
- `Application` define interfaces (`IOrderRepository`, `IPaymentGateway`, `IPushNotifier`);
  `Infrastructure` las implementa. Dependency Inversion, no negociable.
- Un endpoint de `Api` no contiene lógica de negocio: valida entrada, delega en un handler,
  mapea la salida. Si un endpoint tiene un `if` sobre reglas del negocio, está mal ubicado.

## Patrones de diseño que ya sabemos que vamos a usar

No los apliques porque sí; estos tres salen directo del diseño funcional:

- **State** → `Order` tiene 8 estados (`docs/drink.it.v2.md` §5) con transiciones legales
  estrictas. Las transiciones viven en el dominio, no en un `switch` en el servicio.
  Una transición ilegal tira excepción de dominio, no devuelve `false`.
- **Strategy** → los tres métodos de pago (efectivo / digital / saldo VIP) convergen en el
  mismo flujo post-`Paid`. Cada uno es una `IPaymentStrategy`. Agregar un método de pago
  nuevo no debe requerir tocar el flujo (Open/Closed).
- **Domain Events + Observer** → pasar a `Ready` dispara push, actualización SignalR y
  (si es mesa VIP) aparición en la vista del mozo. El dominio publica el evento; los
  handlers de `Application` reaccionan. Nunca llames al servicio de push desde el dominio.

Además: **Result** en vez de excepciones para errores esperables de aplicación (excepciones
sólo para invariantes de dominio rotas).

## Persistencia: repositorio sólo donde hay invariantes

Nada de `IRepository<T>` genérico — con EF Core eso es puro ruido: el `DbContext` ya es un
Unit of Work y `DbSet<T>` ya es un repositorio.

**Escrituras (comandos) → repositorio por agregado.** `IOrderRepository`, `IVipAccountRepository`.
Métodos con intención (`GetForUpdateAsync`, `Add`), no genéricos. Son los únicos lugares con
invariantes que proteger: la máquina de estados de `Order` y el débito de saldo VIP con su
token de concurrencia. El repositorio garantiza que se carga el agregado **completo** y se
guarda atómicamente.

**Lecturas (queries) → EF Core directo, sin repositorio.** La cola del KDS, el menú, las
listas de administración. Un handler de query define su interfaz en `Application`
(`IOrderQueries`) y la implementación en `Infrastructure` usa el `DbContext` con
`AsNoTracking()` y proyección directa a DTO. Cero boilerplate, cero mapeo inútil.

**El `DbContext` nunca se inyecta en `Application` ni en `Domain`.** No es purismo: es lo que
mantiene los tests de `Application` en milisegundos con un doble de prueba, en vez de exigir
Testcontainers para cada test. Si eso se rompe, se rompe el loop de TDD.

## Multi-tenancy: `VenueId` desde el día uno

Decidido el 2026-09-03: la plataforma es multi-tenant aunque arranquemos con un solo boliche.
Esto **no** significa construir la administración de boliches ahora — significa que el modelo
lo permite sin una migración dolorosa después.

- Todo agregado raíz lleva `VenueId`: `Order`, `Product`, `Table`, `VipAccount`, `BarStation`
  y los usuarios internos.
- Un **global query filter** de EF Core aplica el filtro solo. Nadie escribe ese `WHERE` a
  mano, así que nadie se lo puede olvidar.
- Los índices únicos son **compuestos con `VenueId`**: el código corto del pedido y el número
  de mesa son únicos por boliche, no globales. Dos locales tienen que poder tener una "Mesa 5".
- De dónde sale el tenant:
  - **Cliente**: del slug de la ruta (`/{venueSlug}/...`), que viene del QR. Nunca hay una
    pantalla de "elegí tu boliche".
  - **Personal interno**: del claim del token, **nunca** de la URL ni de un parámetro que
    mande el cliente. Un bartender no puede pedir los pedidos de otro local cambiando la ruta.
- Base de datos **compartida con columna discriminadora**. Nada de una base por boliche.
- Tiene que existir un test de integración que pruebe que un pedido de un boliche **no** es
  visible desde otro. Es la invariante más importante del modelo de datos.

## Cómo trabajamos (TDD estricto)

1. **Rojo** — escribí el test que falla primero. Corrélo y mostrame que falla por la razón correcta.
2. **Verde** — la implementación más simple que lo hace pasar.
3. **Refactor** — con los tests en verde.

No escribas implementación sin un test que la exija. Si te pido una feature, arrancá por el
test. Si no tenés claro el comportamiento esperado, preguntá antes de inventar el assert.

Nombres de test, y son dos convenciones distintas a propósito:

- **Backend (xUnit)**: `Method_Scenario_ExpectedResult` —
  `MarkAsReady_WhenOrderIsQueued_ThrowsInvalidTransition`. Hay un método bajo prueba y el
  nombre empieza nombrándolo.
- **Frontend (Vitest)**: `describe` nombra el sujeto e `it` completa la oración, en minúscula
  y sin `should` — `it('cannot be submitted while the form is empty')`, que el runner imprime
  como `StaffLoginPage > cannot be submitted while the form is empty`.

No es inconsistencia: en un test de componente **no hay un método bajo prueba**. Forzar el
formato de C# obliga a inventar un primer segmento, y lo que sale es el label del botón o un
`Render_` que no dice nada. La disciplina es la misma en los dos casos — el nombre tiene que
decir el escenario y el resultado esperado —; lo que cambia es la sintaxis.

## Convenciones

- **Todo el código en inglés, sin excepciones.** Clases, métodos, variables, nombres de
  tabla y columna, **nombres de archivo**, mensajes de log, **los literales de texto y los
  datos de prueba**, y **los comentarios dentro de archivos de código y configuración**
  (`.cs`, `.ts`, `.props`, `.csproj`, `.ps1`, `.json`, `.gitignore`, `.gitattributes`,
  workflows de CI).
  - **Las rutas también van en inglés** (`/:venueSlug/staff/login`, no `/personal/ingresar`).
    Una ruta la lee el router, no una persona: es código.
  - La única excepción son los textos que **ve el usuario final** en la PWA, que van en
    español porque el cliente está en un boliche argentino.
  - **No montamos i18n.** Decidido el 2026-09-11: esos textos van escritos directo en las
    plantillas de Angular, sin archivos de traducción. La app tiene un solo idioma y un solo
    país, y la infraestructura de traducción costaría más de lo que resuelve. Si alguna vez
    hay un segundo idioma, se monta ahí. Lo que **no** cambia: nada de español en `.ts`, `.cs`
    ni en los `id` del DOM — sólo en el texto que se renderiza.
- **Sólo se escribe en español la documentación**: `docs/`, `README.md`, `CLAUDE.md`, los
  archivos de `.claude/`, **los mensajes de commit** y **la descripción de las ramas**. Si
  un archivo lo lee el compilador o una herramienta, va en inglés; si lo lee una persona
  para entender el proyecto, va en español.
- Los documentos de diseño están en español, así que usá **siempre** el glosario de abajo
  para traducir. No inventes sinónimos: si el glosario dice `Order`, no escribas `Purchase`.
- Commits: Conventional Commits (`feat:`, `fix:`, `test:`, `refactor:`, `chore:`). El
  prefijo y el scope son parte del formato y van en inglés; **el título y el cuerpo se
  escriben en español**. Un commit explica *por qué* se hizo el cambio, y eso lo lee el
  equipo, no el compilador: vale la misma regla que para `docs/`. El código que el mensaje
  menciona conserva su nombre real (`LoginHandler`, no "el manejador de login").
- Angular: componentes `standalone`, `signal()` para estado, `inject()` en vez de constructor
  injection, control flow nuevo (`@if`, `@for`). Nada de `NgModule` nuevo, nada de `any`,
  nada de `subscribe()` sin `takeUntilDestroyed`.
- **`if` de una sola sentencia va sin llaves**, y en la misma línea:
  `if (name is null) return false;`. Nunca la variante de dos líneas sin llaves
  (`if (x)` y abajo la sentencia indentada): esa es la que produce el bug de agregar
  una segunda línea que parece estar adentro del `if` y no lo está.
- **No escribas `standalone: true` ni `ChangeDetectionStrategy.OnPush`: los dos son el default.**
  Escribirlos es ruido. Lo que sí hay que justificar en la revisión es un `ChangeDetectionStrategy.Eager`
  (la estrategia vieja, antes llamada `Default`): si aparece uno, preguntá por qué.
- Para traer datos usá la **Resource API** (`httpResource()`, `resource()`, `rxResource()`),
  estable desde v22. Te da los estados de carga y error como signals, que es exactamente lo
  que necesitan nuestras pantallas.
- El cliente HTTP de Angular **se genera del OpenAPI del backend** — no escribas interfaces
  de DTOs a mano en el front, se desincronizan.

## Glosario español → inglés (fuente única de verdad)

Los docs de diseño están en español. El código, en inglés. Esta tabla es el puente; si algo
falta, preguntá antes de inventar el término.

| Docs (español) | Código (inglés) | | Docs (español) | Código (inglés) |
|---|---|---|---|---|
| Pedido | `Order` | | Boliche | `Venue` |
| Ítem del pedido | `OrderItem` | | Barra / estación | `BarStation` |
| Trago / producto | `Product` | | Ticket | `Ticket` |
| Menú | `Menu` | | Cliente | `Customer` |
| Mesa | `Table` | | Cajero | `Cashier` |
| Cuenta VIP | `VipAccount` | | KDS (estación de barra) | `Kds` |
| Saldo | `Balance` | | Mozo | `Waiter` |
| Método de pago | `PaymentMethod` | | Retiro en barra | `BarPickup` |
| Efectivo | `Cash` | | Entrega en mesa | `TableDelivery` |
| Pago digital | `DigitalPayment` | | Suscripción push | `PushSubscription` |
| Usuario interno | `StaffUser` | | Rol | `StaffRole` |
| Administrador | `Administrator` | | Baja lógica | `IsActive` |

> **`Product`, no `Drink`.** Decidido el 2026-09-14: la carta vende tragos pero también
> botellas, y el nombre tiene que cubrir las dos cosas. Los docs siguen diciendo "trago"
> porque así lo dice la consigna; en el código es siempre `Product`.

**Estados de `Order`** (§5 del diseño funcional):
`Cart` · `AwaitingPayment` · `Paid` · `Queued` · `InPreparation` · `Ready` · `Delivered` · `Canceled`

(`Canceled` con una sola `l`, como el resto de la BCL de .NET.)

## Sólo librerías gratuitas — regla dura

Toda dependencia tiene que ser **gratis y de código abierto para uso comercial, sin tier
pago ni licencia "community" con condiciones**. Antes de proponer cualquier paquete,
verificá la licencia vigente hoy, no la que tenía hace dos años.

Trampas concretas de este stack (populares en tutoriales, ya no libres o con condiciones):

- **MediatR** y **FluentAssertions** — pasaron a modelo comercial. Alternativas: handlers
  explícitos inyectados por DI (menos magia y más SOLID visible), y **Shouldly** o los
  asserts nativos de xUnit.
- **AutoMapper** — mismo caso. Escribí el mapeo a mano; en un proyecto de este tamaño es
  más corto que configurar el mapeador, y falla en tiempo de compilación.
- **Telerik**, **Syncfusion**, **DevExpress**, **Kendo** — pagos. La "community license" de
  Syncfusion tiene límites de facturación y de tamaño de equipo: no cuenta como gratis.
- **Cypress Cloud**, **BrowserStack**, **Sentry** en tier pago — usamos Playwright local y
  Application Insights.

Verde para nosotros: MIT, Apache-2.0, BSD. Ante cualquier duda, proponelo y esperá el OK.

## Flujo de ramas

```
feature  ──PR──►  dev  ──PR──►  main
```

- `main` — **la única rama que despliega.** Es lo que corre en Azure. Sólo recibe merges
  desde `dev`, nunca un feature directo.
- `dev` — rama de integración, **sin ambiente desplegado**. Es la **rama por defecto** del
  repo en GitHub, así ningún PR apunta a `main` por descuido.
- `<prefijo>/<issue>-descripcion-corta` — sale de `dev` y vuelve a `dev` por PR.

Prefijos: `feat/`, `fix/`, `chore/`, `refactor/`, `test/`. **El prefijo es la parte
reservada y va siempre en inglés; la descripción que sigue se escribe en español**
(`feat/12-autenticacion-de-personal`). Sin tildes ni `ñ`: el nombre de una rama viaja por
URLs, nombres de archivo y consolas de tres sistemas operativos, y ahí un carácter no ASCII
sólo trae problemas. Escribí `anio`, no `año`.

> Hay **un solo ambiente** en Azure, alimentado desde `main`
> ([ADR-0007](docs/adr/0007-hosting-en-azure-a-costo-cero.md)). Mantener un staging aparte
> duplicaría el consumo de los tiers gratuitos sin que nadie lo use.

Estrategia de merge, y no es un detalle:

- feature → `dev`: **squash**. Un commit por feature; la historia de `dev` se lee como una
  lista de features, no como 40 commits de "wip".
- `dev` → `main`: **merge commit**. Preserva los features individuales que entraron al release.

La rama se borra después del merge.

**Hotfix:** `hotfix/` sale de `main`, vuelve a `main` y **se mergea de vuelta a `dev` en el
acto**. Si eso se saltea, el arreglo desaparece en el próximo merge de `dev` a `main` y el
bug vuelve a producción.

## Definition of Done

Una feature está lista cuando: tests unitarios verdes · test de integración si toca la DB
o un servicio externo · `dotnet format` y `pnpm run lint` limpios · sin bajar la cobertura ·
tests de arquitectura verdes · si es una pantalla del flujo principal, tiene spec de Playwright.

## Cosas que NO tenés que hacer sin que te lo pida

- Correr migraciones de EF contra una base que no sea local.
- **Hacer `git commit`.** Dejá los cambios preparados, mostrame exactamente qué archivos
  entran y con qué mensaje, y esperá el OK. El commit lo confirmo yo, siempre, aunque
  antes en la misma conversación haya dicho "vayamos commiteando por paso".
- `git push`, abrir PRs o mergear.
- Revertir, resetear o cambiar de rama sin avisar.
- Agregar dependencias nuevas de NuGet o npm. Proponelas **con la licencia verificada**
  (ver la regla de librerías gratuitas) y esperá el OK — somos 3 y cada dependencia es
  deuda compartida.
- Tocar `infra/` (Bicep) o los workflows de deploy.
