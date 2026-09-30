# Pagos con Mercado Pago en tu máquina

Guía para dejar funcionando el **pago digital con Mercado Pago (Checkout Pro)** en tu
computadora y poder probarlo de punta a punta: pedir, pagar con una tarjeta de prueba, volver a
la app y ver el pedido en el tablero de la barra.

> **Mercado Pago está en modo prueba**, y va a seguir así por mucho tiempo. Nada de lo que hagas
> con esta guía cobra plata real: el token es de un vendedor de prueba, las tarjetas son de
> prueba y la app **no tiene producción activada**.

## Cómo funciona, en una línea por paso

1. El cliente toca **Pagar con Mercado Pago**. El pedido se crea en `AwaitingPayment` y **no**
   entra a la barra.
2. La API le pide a Mercado Pago un cobro (una *preferencia*) que vence en **15 minutos**, y la
   PWA manda al cliente a pagar en la página de Mercado Pago.
3. El resultado llega por dos caminos, y cualquiera de los dos alcanza:
   - **La vuelta del cliente:** Mercado Pago lo devuelve a `/{boliche}/orders/{código}/{token}/payment`.
   - **El webhook:** Mercado Pago avisa a `/{boliche}/payments/notifications`, firmado.
4. La API consulta el pago **a Mercado Pago** (nunca le cree a la URL) y mueve el pedido:

   | Pago en Mercado Pago | Pedido |
   | --- | --- |
   | Aprobado | `Paid` → entra a la cola de la barra |
   | Rechazado o cancelado | `Canceled`, y los tragos vuelven al stock |
   | Pendiente o en revisión | Sigue esperando (con el modo binario casi no pasa) |
   | El cliente volvió sin pagar | `Canceled`, y los tragos vuelven al stock |
   | Pasaron 15 minutos sin pago | `Canceled`, y los tragos vuelven al stock |

El diseño completo está en [sprint-2-backlog.md](sprints/sprint-2-backlog.md), US-24.

## Lo que necesitás antes de empezar

| Qué | De dónde sale |
| --- | --- |
| Lo de siempre: .NET, Node, pnpm y Docker | [README de e2e](../frontend/e2e/README.md#requisitos) |
| El **access token de prueba** de la app | Pedíselo a Eugenia **por privado** (nunca por el grupo ni por un issue) |
| El **usuario y la contraseña del comprador de prueba** | Pedíselos a Eugenia por privado |
| `cloudflared`, sólo para la parte del túnel | `brew install cloudflared` (gratis, Apache-2.0) |

La cuenta de Mercado Pago es la de Eugenia. La app se llama **drinkit pruebas** (ID
`8240740196905075`). Los usuarios de prueba cuelgan de la app `3308656819990493`, que Mercado
Pago creó sola al generarlos.

## 1. Cargar el token de prueba

El token **no va en ningún archivo del repo**: ni en `appsettings.json`, ni en un
`appsettings.Development.json`, ni en un `.env`. Va en el almacén de secretos de desarrollo de
.NET (`dotnet user-secrets`), que vive fuera de la carpeta del proyecto (en
`~/.microsoft/usersecrets/`) y que la API lee sola cuando corre en Development.

Desde la raíz del repo, una sola vez:

```bash
dotnet user-secrets set "MercadoPago:AccessToken" "APP_USR-…" --project backend/src/DrinkIt.Api
```

Para comprobar que quedó, sin mostrarlo en pantalla:

```bash
dotnet user-secrets list --project backend/src/DrinkIt.Api | sed 's/=.*/= (cargado)/'
```

> El plugin de Mercado Pago de Claude Code **bloquea** escribir el token en un archivo del
> proyecto, aunque esté en `.gitignore`. No es un error: por eso se usa `user-secrets`.

## 2. Levantar todo como siempre

```bash
docker compose up -d
dotnet run --project backend/src/DrinkIt.Api
pnpm --prefix frontend start
```

Con esto ya podés pedir y llegar a la página de pago de Mercado Pago. **Pero sin el túnel del
paso 3, después de pagar Mercado Pago no te devuelve a la app**, y el pedido se cancela solo a
los 15 minutos aunque lo hayas pagado. El motivo está en la sección
[Por qué hace falta un túnel](#por-qué-hace-falta-un-túnel).

## 3. Abrir un túnel HTTPS (para que Mercado Pago pueda volver)

> ⚠️ **Todavía no se probó de punta a punta.** Los pasos siguen la configuración real del
> proyecto (el proxy de `ng serve` y las opciones de la API), pero el primero que lo haga, que
> anote acá cualquier ajuste.

1. En una terminal aparte, abrí el túnel hacia el `ng serve`:

   ```bash
   cloudflared tunnel --url http://localhost:4200
   ```

   Te da una dirección tipo `https://algo-al-azar.trycloudflare.com`. **Cambia cada vez** que
   abrís el túnel.

2. Decile a la API que ésa es la dirección pública. Las dos claves apuntan al mismo túnel: la API
   se alcanza por `/api` gracias al proxy de `ng serve`.

   ```bash
   dotnet user-secrets set "MercadoPago:PublicAppUrl" "https://algo-al-azar.trycloudflare.com" --project backend/src/DrinkIt.Api
   dotnet user-secrets set "MercadoPago:PublicApiUrl" "https://algo-al-azar.trycloudflare.com/api" --project backend/src/DrinkIt.Api
   ```

   Reiniciá la API.

3. Levantá el front como siempre (`pnpm --prefix frontend start`). `angular.json` ya acepta
   las direcciones `*.trycloudflare.com` en desarrollo; cualquier otro host sigue bloqueado.

4. **Abrí la app por la dirección del túnel**, no por `localhost:4200`:
   `https://algo-al-azar.trycloudflare.com/bar-alfa/menu`.

Con una dirección pública HTTPS, Mercado Pago sí te devuelve a la app y además activa el
`auto_return`: la vuelta es automática, sin tocar "Volver al sitio".

### El webhook (opcional)

La vuelta del cliente alcanza para mover el pedido. El webhook cubre al cliente que cierra el
navegador antes de volver. Para probarlo:

1. **Registrá la URL de modo prueba** en la app *drinkit pruebas* → **Webhooks → Configurar
   notificaciones → Modo prueba**, con el tópico **Pagos**:

   ```text
   https://algo-al-azar.trycloudflare.com/api/bar-alfa/payments/notifications
   ```

   También se puede hacer desde Claude Code con el plugin de Mercado Pago (`/mp-integrate
   webhook`). Así se hizo la primera vez, el 2026-09-30.

2. **Cargá la clave secreta.** Se ve en esa misma pantalla, revelándola. Para quien no tenga
   acceso al panel, pedírsela a Eugenia por privado:

   ```bash
   dotnet user-secrets set "MercadoPago:WebhookSecret" "…" --project backend/src/DrinkIt.Api
   ```

   Reiniciá la API.

> **La URL del túnel cambia cada vez que lo abrís**, así que el paso 1 se repite en cada sesión.
> La clave secreta no cambia. En Azure la dirección es fija y esto se configura una vez.
>
> **Sin verificar todavía:** si dos personas prueban a la vez. Cada cobro manda su propia
> `notification_url` al túnel de quien lo creó, así que lo esperable es que cada uno reciba lo
> suyo; la URL del panel es una sola por app y funciona como respaldo.

Sin la clave, la API **rechaza todas las notificaciones** con 401: una notificación que no se
puede verificar no se cree.

Cada cobro además le manda a Mercado Pago su propia `notification_url`, con el slug del boliche.
La URL del panel es la de respaldo y la que genera la clave.

## 4. Pagar

1. Abrí la app **en una ventana de incógnito** (ver *Problemas frecuentes*), pedí algo y tocá
   **Pagar con Mercado Pago**.
2. En la página de Mercado Pago, entrá con el **comprador de prueba** (usuario y contraseña que
   te pasó Eugenia).
3. Pagá con una tarjeta de prueba. **El nombre del titular decide el resultado:**

   | Titular | Resultado |
   | --- | --- |
   | `APRO` | Aprobado |
   | `OTHE` | Rechazado |
   | `CONT` | Rechazado: el cobro pide **modo binario** (aprobado o rechazado, nunca pendiente) |
   | `FUND` | Rechazado por fondos insuficientes |

   | Tarjeta | Número | Vencimiento | CVV |
   | --- | --- | --- | --- |
   | Visa crédito | `4509 9535 6623 3704` | `11/30` | `123` |
   | Mastercard crédito | `5031 7557 3453 0604` | `11/30` | `123` |

   Documento: DNI `12345678`.

## 5. Qué tenés que ver

| Pagaste con | En la app del cliente | En la barra (`/bar-alfa/staff/kds`) |
| --- | --- | --- |
| `APRO` | Vuelve y va al seguimiento: "Ya está pago y esperando en la barra" | El pedido aparece en **Nuevos**, en vivo |
| `OTHE` | "El pago no se completó y el pedido se canceló", con **Volver a intentar** (los tragos siguen en el carrito) | Nada; el stock vuelve a la carta |
| `CONT` | Igual que `OTHE`: con modo binario un pago no queda pendiente | Nada |
| Volviste sin pagar | Igual que `OTHE` | Nada |

## Qué ve el cliente en Mercado Pago

El cobro va configurado así, por la revisión de calidad de Mercado Pago (`/mp-review`):

- **Modo binario:** el pago se aprueba o se rechaza en el momento; nunca queda pendiente.
- **Sin Rapipago, Pago Fácil ni transferencia por cajero** (`ticket` y `atm`): se pagan horas
  después, y un pedido espera 15 minutos.
- **En una sola cuota.**
- Van el **nombre y apellido** del cliente, el **id y la descripción** de cada trago (con su
  nota) y **DRINKIT** como texto del resumen de la tarjeta.

## Tests que usan Mercado Pago

| Qué | Comando | Sin token |
| --- | --- | --- |
| Todo el backend | `dotnet test backend/DrinkIt.slnx` | Los de Mercado Pago se saltean solos |
| Sólo el contrato con Mercado Pago | `dotnet test backend/tests/DrinkIt.Api.IntegrationTests --filter "FullyQualifiedName~MercadoPagoContractTests"` | Se saltean |

El **contrato** (`MercadoPagoContractTests`) le habla a la API real de Mercado Pago con tu token
de prueba: crea preferencias de verdad y las lee de vuelta. Es lo que detecta si alguna vez
rompemos cómo le hablamos a Mercado Pago. De ahí salieron dos cosas que la documentación no
decía:

- **Mercado Pago descarta las URL de vuelta `http://localhost`** sin avisar.
- **No deduplica preferencias:** pedir dos veces el cobro del mismo pedido abre dos. Por eso el
  pedido guarda su link de pago y un reintento devuelve ése.

## Por qué hace falta un túnel

Mercado Pago está en internet y tu máquina no. Hay dos cosas que necesitan llegar de Mercado Pago
a vos:

- **La vuelta del cliente.** Mercado Pago **ignora** cualquier URL de vuelta que no sea HTTPS
  pública (lo verifica el test `StartCheckoutAsync_WithALocalReturnAddress_MercadoPagoDropsIt`).
  Sin túnel, después de pagar el cliente se queda en la pantalla de Mercado Pago.
- **El webhook.** Mercado Pago tiene que poder llamar a tu API. Sin dirección pública, la API ni
  siquiera se la pasa (`notification_url` queda vacío).

En Azure la API y la PWA tienen direcciones públicas HTTPS, así que ahí no hace falta ningún
túnel.

## Problemas frecuentes

| Qué pasa | Por qué | Qué hacer |
| --- | --- | --- |
| Al pagar dice **503** / "Mercado Pago no respondió" | No hay token cargado, o está mal | Paso 1, y reiniciá la API |
| Pagaste y **no volviste a la app** | Estás sin túnel: Mercado Pago descartó la URL de vuelta | Paso 3 |
| El pedido pagado **se canceló solo** | Pasaron 15 minutos sin que llegara el resultado (sin túnel ni webhook) | Paso 3 |
| Mercado Pago **no te deja pagar** o da error al confirmar | Estás logueado en Mercado Pago con otra cuenta (por ejemplo la del vendedor) en el mismo navegador | Pagá en una ventana de incógnito, entrando con el comprador de prueba |
| El webhook contesta **401** | Falta la clave secreta o no coincide | Sección *El webhook* |
| Por el túnel aparece **"Blocked request"** (403) | El túnel no es de `trycloudflare.com`, o `ng serve` arrancó antes del cambio en `angular.json` | Usá `cloudflared` y reiniciá `ng serve`. El flag `--allowed-hosts` del CLI es sólo `true`/`false` y `true` abre todo: no lo uses |
| Los e2e de Playwright **fallan al pagar** | Con token, el checkout va a Mercado Pago de verdad | Ver la nota de abajo |

> **Nota sobre los e2e.** Los tests de Playwright que crean pedidos (checkout, seguimiento, KDS,
> escaneo) pagan por la interfaz. Con el pago real, esos pedidos pasan por la página de Mercado
> Pago. Cómo resolverlo (una pasarela simulada sólo para esos tests, y un e2e aparte que pague de
> verdad) está en discusión en US-24.

## Seguridad

- **Nunca** pongas el token ni la clave de webhooks en un archivo del repo, en un commit, en un
  issue ni en el chat del grupo.
- Si un token se filtra, se renueva desde el panel de Mercado Pago y se avisa al equipo.
- Producción **no está activada** en la app. Antes de activarla hay que pasar `/mp-review` y el
  formulario de homologación de Mercado Pago.
