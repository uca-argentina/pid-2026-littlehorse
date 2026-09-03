---
name: feature-vertical
description: Workflow TDD de punta a punta para implementar una feature completa de drink.it, desde el dominio hasta la pantalla de Angular y el spec E2E. Usalo cuando se pide implementar una funcionalidad nueva que atraviesa backend y frontend, no para cambios chicos de una sola capa.
---

# Feature vertical (dominio → API → PWA → E2E)

Una feature se construye de adentro hacia afuera, en rebanadas verticales delgadas. Nunca
"todo el backend y después todo el frontend": eso esconde los errores de contrato hasta el final.

## Paso 0 — Anclar en el diseño funcional

Antes de escribir nada, ubicá la feature en `docs/drink.it.v2.md` y citá la sección. Si el
comportamiento no está definido ahí (mirá §15, "Preguntas abiertas"), **preguntá antes de
implementar** — hay decisiones de producto sin cerrar y adivinar cuesta más que preguntar.

## Paso 1 — Dominio (rojo → verde → refactor)

1. Escribí el test en `DrinkIt.Domain.Tests` que describe la regla de negocio. Corrélo.
   **Mostrá el fallo** antes de seguir.
2. Implementá lo mínimo en `DrinkIt.Domain`.
3. Refactorizá con el test en verde.

El dominio no conoce base de datos, HTTP ni Angular. Si el test necesita un mock, algo está
mal ubicado.

## Paso 2 — Caso de uso

1. Test en `DrinkIt.Application.Tests` con dobles de prueba de los puertos
   (`IOrderRepository`, `IPaymentGateway`, ...).
2. Handler en `DrinkIt.Application`. Si necesitás un servicio externo nuevo, **definí la
   interfaz acá** e implementala después en `Infrastructure`.

El test de aplicación verifica orquestación y errores esperables, no reglas de negocio
(esas ya están cubiertas en el paso 1).

## Paso 3 — Infraestructura y endpoint

1. Implementá los puertos nuevos en `DrinkIt.Infrastructure`.
2. Endpoint fino en `DrinkIt.Api`: validar entrada, delegar, mapear salida.
3. Test de integración en `DrinkIt.Api.IntegrationTests` (WebApplicationFactory +
   Testcontainers) que recorre el camino real, incluida la persistencia.
4. Si agregaste una entidad o cambiaste el esquema, generá la migración de EF y **revisá el
   SQL generado** antes de darla por buena.

## Paso 4 — Contrato

Regenerá el cliente TypeScript desde el OpenAPI. No escribas los DTOs a mano en el front.
Si el contrato quedó feo de consumir, arreglá el endpoint ahora, no el front después.

## Paso 5 — Frontend

1. Test del servicio o store con el cliente generado mockeado.
2. Test del componente: comportamiento observable, no estructura del template.
3. Componente `standalone` con `signal()` (OnPush ya es el default en Angular 22).
4. Estados de carga, error y vacío. En un boliche la red falla — no es un caso borde.

## Paso 6 — E2E

Si la feature está en el flujo principal de `docs/flujo-pedido.md`, agregá un spec de
Playwright que recorra el camino feliz completo desde el navegador.

## Antes de decir "listo"

    dotnet test backend/DrinkIt.slnx
    dotnet format backend/DrinkIt.slnx --verify-no-changes
    npm --prefix frontend run lint
    npm --prefix frontend run test

Reportá qué corriste y qué dio. Si algo falla, decilo con la salida — no lo escondas.
