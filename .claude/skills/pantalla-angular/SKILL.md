---
name: pantalla-angular
description: Crear una pantalla o componente nuevo en la PWA Angular de drink.it siguiendo las convenciones del proyecto (standalone, signals, Resource API, estados de carga/error, accesibilidad y usabilidad nocturna). Usalo cuando se pide una vista, pantalla o componente del frontend.
---

# Pantalla de Angular

## Dónde va

    frontend/src/app/features/<feature>/
      <feature>.routes.ts          # ruta lazy
      pages/<nombre>.page.ts       # componente contenedor (smart)
      components/                  # componentes de presentación (dumb)
      <feature>.store.ts           # estado con signals
      <feature>.service.ts         # llamadas HTTP vía el cliente generado

Rutas siempre lazy (`loadChildren` / `loadComponent`). El cliente entra a esta PWA desde
un QR con datos móviles en un boliche: cada KB del bundle inicial se paga en segundos de espera.

## Forma del componente

- `standalone: true`. NO escribas `ChangeDetectionStrategy.OnPush`: desde Angular 22 es el
  default. Un `ChangeDetectionStrategy.Eager` (la vieja `Default`) hay que justificarlo.
- `inject()` en vez de inyección por constructor.
- Estado con `signal()` y `computed()`. Nada de `BehaviorSubject` para estado local.
- Control flow nuevo: `@if`, `@for` (con `track` siempre), `@switch`.
- `input()` / `output()` de función, no los decoradores viejos.
- Componente contenedor: obtiene datos y coordina. Componentes de presentación: reciben
  inputs y emiten outputs, sin inyectar servicios.

## Los cuatro estados, siempre

Toda pantalla que trae datos modela **cargando / con datos / vacío / error**. En este contexto
la red se cae de verdad: un spinner infinito es un bug, no un detalle.

La forma corta de conseguirlo es la **Resource API** (`httpResource()`), estable desde
Angular 22: expone `value()`, `isLoading()` y `error()` como signals, así no armás el manejo
de estado a mano ni te olvidás de una rama.

Para el error, mostrá una acción de reintento, no sólo un mensaje.

## Tiempo real (pantalla de estado del pedido y KDS)

- Suscripción a SignalR encapsulada en un servicio, nunca en el componente.
- Manejar **desconexión y reconexión**: al reconectar, re-sincronizar el estado completo
  contra la API, no asumir que no pasó nada mientras estabas offline.
- El estado del pedido nunca se sirve desde el cache del service worker.

## Usabilidad en el boliche (no es opcional)

- Áreas táctiles de 44px como mínimo. Se usa con una mano, empujado, con la otra ocupada.
- Contraste alto: pantalla al máximo en un ambiente oscuro.
- El color nunca es el único canal de información (el semáforo de antigüedad del KDS
  necesita también texto o posición).
- Confirmación en toda acción irreversible.
- Textos cortos y grandes. Nadie lee un párrafo en la fila de la barra.

## Accesibilidad mínima exigible

Nombre accesible en cada control interactivo, `label` en cada input, foco manejado al
navegar, y que se pueda operar con teclado (importa para el KDS y la caja, que se usan
con lector de QR conectado por HID).

## Tests primero

1. Test del store o servicio con el cliente generado mockeado.
2. Test del componente con Testing Library: verificá **lo que ve y hace el usuario**
   (texto en pantalla, click que dispara la acción), no la estructura del DOM ni los
   internals del componente.
3. Si la pantalla está en el flujo de `docs/flujo-pedido.md`, después va el spec de Playwright.

## Nunca

- Escribir a mano interfaces de DTOs que ya vienen del cliente generado por OpenAPI.
- `any`, o un `as` que tapa un error de tipos.
- `subscribe()` sin `takeUntilDestroyed()`.
- Lógica de negocio en el componente.
