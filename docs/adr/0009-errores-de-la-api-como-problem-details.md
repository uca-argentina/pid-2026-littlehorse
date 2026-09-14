# ADR-0009: Los errores de la API viajan como Problem Details (RFC 9457)

- **Estado:** aceptada
- **Fecha:** 2026-09-09

## Contexto

La PWA tiene que reaccionar distinto según por qué falló un request: un pedido rechazado por
saldo insuficiente no se muestra igual que una sesión vencida. Para eso necesita un
identificador **estable** del problema, no el texto del mensaje —que se reescribe— ni el
status HTTP —que es demasiado grueso: el Sprint 1 ya tiene varios `409` distintos
(transición de estado ilegal, pedido ya pagado) y todos comparten status.

RFC 9457 (que reemplaza al 7807) define ese formato: `application/problem+json` con los
campos `type`, `title`, `status`, `detail` e `instance`.

El punto que se nos pasó al principio: **`type` es una URI reference**, y el RFC resuelve las
relativas contra la URI base del documento. El endpoint de login mandaba el código pelado
(`auth.invalid_credentials`), que parsea como referencia relativa y por lo tanto nadie
rechaza — pero significa una cosa distinta según qué host respondió, que es exactamente lo
contrario de un identificador estable.

## Opciones evaluadas

**Para el formato del `type`:**

| Opción | Trade-off |
|---|---|
| El código pelado (`auth.invalid_credentials`) | No cumple el RFC. Se resuelve distinto por host. |
| Una URL (`https://drink.it/problems/...`) | Nos compromete con un dominio que hay que sostener, y deja un link muerto que alguien va a reportar como bug. |
| **Un URN (`urn:drinkit:problem:auth:invalid-credentials`)** | URI absoluta válida, nunca se rompe, no requiere hostear nada. RFC 9457 dice explícitamente que el consumidor **no** debe asumir que el `type` se puede dereferenciar. |

**Para los errores que no nacen en un endpoint** (excepción no manejada, 404 de ruteo, 401 de
JwtBearer): sin `UseExceptionHandler` ni `UseStatusCodePages` esas respuestas salen con el
**body vacío**, así que el front tendría que manejar dos contratos para la misma falla.

## Decisión

**Un URN por tipo de problema del dominio, y el default del framework para el resto.**

- `ProblemTypes.For(error.Code)` es el **único** lugar que convierte un `Error.Code` de
  `Application` en la URI del `type`. `auth.invalid_credentials` →
  `urn:drinkit:problem:auth:invalid-credentials`. Nunca se pasa `error.Code` directo.
- `Error.Code` sigue siendo el código con puntos: `Application` no tiene por qué saber qué es
  una URI.
- **Cuando no hay un problema propio del dominio, no se declara `type`.** ASP.NET Core lo
  completa con el link a la sección del RFC 9110 que corresponde al status. Eso no agrega
  nada que no esté ya en `status`, y por eso sirve justo donde no hay nada más específico que
  decir: el 500 genérico, el 404 de ruteo, el 415.
- `UseExceptionHandler` + `UseStatusCodePages` van como **primer middleware**, así que toda
  respuesta de error sale en `problem+json`, la genere un endpoint o el framework.
- **`exception.Message` nunca va en el `detail`.** El mensaje de una `SqlException` nombra
  tablas y columnas, y el de una `DbUpdateException` a veces trae la cadena de conexión. El
  `detail` es un texto fijo y el `traceId` es lo que correlaciona con el log que sí tiene la
  causa. Hay un test que lo verifica filtrando a propósito primero.
- **La única excepción es `DomainException`, y es un contrato.** Una invariante de dominio
  rota vuelve como `400` con `type` = `ProblemTypes.For(Code)` y `detail` = `Message`, para que
  el formulario pueda señalar el campo. Eso convierte al mensaje de **toda** `DomainException`
  en texto que ve el cliente, hoy y para siempre: se escribe en el dominio, no puede nombrar
  tablas, columnas ni nada de infraestructura, y no puede depender de datos que el cliente no
  mandó. Si un mensaje no cumple eso, no va en una `DomainException`.

  Es un *fallback*, no el camino normal: el formulario y el endpoint validan antes, así que
  llegar al dominio con un dato inválido es una señal de que algo aguas arriba se saltó la
  validación. Por eso se loguea como `Warning` con el código de la regla.
- El `instance` es el path, no `"GET /orders"`: el RFC lo tipa como URI reference igual que
  el `type`, y ahí un espacio no es un carácter legal.

Tipos definidos hasta hoy:

| Situación | `type` |
|---|---|
| Usuario o contraseña incorrectos | `urn:drinkit:problem:auth:invalid-credentials` |
| Token vencido a mitad del turno | `urn:drinkit:problem:auth:session-expired` |
| Request sin token | `urn:drinkit:problem:auth:authentication-required` |

Los tres son `401`, y distinguirlos es lo que le permite a la PWA mandar al usuario al login
con un mensaje que se entienda. Eso importa más por [ADR-0008](0008-autenticacion-con-token-unico-sin-refresh-token.md):
con token único y sin refresh, una sesión vencida es un evento normal de fin de turno.

## Consecuencias

- Un endpoint que ya nombró su problema **no se pisa** más adelante en el pipeline. Si no,
  el 401 del login se convertiría en "sesión vencida" y se borraría la única distinción que
  esto existe para hacer.
- El cliente HTTP de Angular se genera del OpenAPI, así que estos `type` son parte del
  contrato: renombrar un `Error.Code` cambia la URI. Hay un test que fija los códigos que ya
  se publicaron para que un renombre aparezca como test roto y no como cambio silencioso.
- En Development, `WebApplication` pone la developer exception page por delante, así que un
  crash sigue mostrando su stack trace en la máquina del desarrollador. Sólo la API
  desplegada responde el body genérico.

## Lo que esta decisión no cubre

Registrar los tipos de problema en el IANA registry que define RFC 9457 §4.2. Es para tipos
que van a usar terceros; los nuestros los consume una sola PWA.
