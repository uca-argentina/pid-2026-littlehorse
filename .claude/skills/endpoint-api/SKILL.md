---
name: endpoint-api
description: Crear o modificar un endpoint HTTP del backend .NET de drink.it siguiendo las convenciones del proyecto (Minimal API fina, handler en Application, validacion, OpenAPI, tests). Usalo cuando se pide agregar, cambiar o exponer un endpoint de la API.
---

# Endpoint de la API

## Estructura por feature, no por tipo

Los endpoints se agrupan por feature vertical, no en un `Controllers/` gigante:

    DrinkIt.Api/Features/Orders/CreateOrderEndpoint.cs
    DrinkIt.Api/Features/Orders/MarkOrderReadyEndpoint.cs
    DrinkIt.Api/Features/Cashier/ConfirmCashPaymentEndpoint.cs

Cada archivo registra su ruta con un método de extensión sobre `IEndpointRouteBuilder`.

## El endpoint es fino

Un endpoint hace exactamente cuatro cosas:

1. Recibir y validar la forma de la entrada (FluentValidation o validación mínima).
2. Delegar en un handler de `DrinkIt.Application`.
3. Mapear el `Result` del handler al status HTTP correcto.
4. Nada más.

Si aparece un `if` sobre una regla del negocio, va al dominio. Si aparece un acceso a
`DbContext`, va a un repositorio.

## Mapeo de errores a HTTP

- Validación de forma → `400` con `ValidationProblemDetails`.
- Recurso inexistente → `404`.
- Transición de estado ilegal, saldo VIP insuficiente, pedido ya pagado → `409 Conflict`
  con `ProblemDetails` y un `type` estable que el front pueda discriminar.
- No autenticado / sin rol → `401` / `403`.
- Excepción no manejada → `500` genérico, **sin filtrar detalles internos**, con el
  `traceId` correlacionable en los logs.

Usá siempre `ProblemDetails` (RFC 7807). El front necesita un código estable, no un string
de mensaje que va a cambiar.

## Autorización

Los endpoints de cliente son anónimos por diseño (§14: el cliente no tiene cuenta) pero
**el identificador del pedido es la credencial**: usá un id no adivinable (GUID o ULID),
nunca un entero secuencial. Los de caja, bartender, mozo y admin exigen rol.

## OpenAPI

Todo endpoint declara sus tipos de respuesta (`Produces<T>`, `ProducesProblem`) porque de
ahí se genera el cliente de Angular. Un endpoint mal documentado rompe el front en silencio.

## Tests, en este orden

1. **Primero** el test de integración que falla, en `DrinkIt.Api.IntegrationTests`:
   camino feliz + al menos el error de negocio principal (ej. marcar listo un pedido que
   todavía no se pagó).
2. Recién después el endpoint.
3. Verificá que el status y el `ProblemDetails` sean exactamente los esperados, no sólo
   que "no explote".

## Idempotencia

Los endpoints que disparan efectos irreversibles (confirmar cobro, marcar entregado,
descontar saldo VIP) tienen que ser seguros ante un doble tap o un reintento de red.
En un boliche el cajero va a tocar dos veces. Diseñalo para que la segunda llamada
devuelva el mismo resultado en vez de cobrar dos veces.
