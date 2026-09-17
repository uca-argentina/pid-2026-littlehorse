---
name: spec-e2e
description: Escribir o corregir specs E2E de Playwright para drink.it, incluyendo los recorridos multi-actor (cliente + bartender + cajero + mozo) y las condiciones reales de un boliche. Usalo cuando se pide un test end-to-end, un test de flujo completo, o cuando un spec E2E falla.
---

# Specs E2E con Playwright

Los E2E son caros y frágiles. Escribí pocos y que cubran lo que realmente importa: los
recorridos de `docs/flujo-pedido.md`.

## Qué merece un E2E

Sí: los tres caminos de pago hasta "Entregado" (efectivo, digital, saldo VIP), y el
fallback de estado en tiempo real cuando el push no llega.

No: validaciones de formulario, permutaciones del menú, casos borde de cálculo. Eso son
tests unitarios y cuestan cien veces menos.

## Lo particular de esta app: son multi-actor

Un recorrido completo involucra al cliente en su celular, al bartender en la tablet y a
veces al cajero o al mozo. Modelalo con **varios browser contexts en el mismo test**, cada
uno con su propio estado y viewport:

    const customer = await browser.newContext({ ...devices['iPhone 13'] });
    const tablet   = await browser.newContext({ viewport: { width: 1280, height: 800 } });

Así el test verifica de verdad la coordinación en tiempo real entre pantallas, que es
exactamente donde esta app puede romperse.

## Nombres

Igual que los unitarios del frontend: el `describe` nombra el sujeto y el `test` completa la
oración, en minúscula y sin `should` — `test('opens the staff area for the seeded
administrator')`. No se usa `Sujeto_Escenario_ResultadoEsperado`: ese formato es del backend,
donde hay un método bajo prueba que nombrar. Ver `CLAUDE.md`.

## Selectores

Sólo roles accesibles (`getByRole`, `getByLabel`, `getByText`) o `data-testid`. Nunca
selectores de CSS estructurales ni clases de estilo: se rompen con cada refactor de UI y
generan tests que fallan sin que haya un bug.

## Nada de esperas fijas

Cero `waitForTimeout`. Usá los auto-waits de Playwright y asserts con `expect().toPass()` o
`toHaveText`. Un `sleep` en un E2E es un test flaky esperando su turno.

## Cosas de este dominio que hay que cubrir explícitamente

- **iOS/Safari**: corré al menos un proyecto con WebKit. Es la plataforma donde el push no
  funciona sin instalar la PWA (§9.2), y el fallback de tiempo real tiene que andar ahí.
- **Push denegado**: simular el rechazo del permiso y verificar que el flujo sigue
  funcionando con la pantalla de estado en tiempo real.
- **Doble tap**: el cajero confirmando un cobro dos veces no puede cobrar dos veces.
- **Red intermitente**: cortar la red con `context.route()` y verificar que la app avisa y
  se recupera al volver.
- **Lector de QR**: el lector es un teclado HID. Se simula tipeando el código en el input
  enfocado y mandando `Enter` — no hace falta emular cámara.

## Datos de test

Cada spec crea sus propios datos vía API (no clickeando toda la UI para llegar al estado
inicial) y limpia lo suyo. Nunca dependan de datos compartidos ni del orden de ejecución:
hoy corren con un solo worker porque `ng serve` no aguanta varios (ver el comentario en
`playwright.config.ts`), pero eso va a cambiar cuando corran contra el build de producción, y
un test que asume orden se rompe justo ese día.

## Cuando un spec falla

Leé el trace (`pnpm --prefix frontend exec playwright show-trace`) antes de tocar el test.
Si el test es correcto y la app está mal, el bug es de la app — no relajes el assert para
que pase. Decilo.
