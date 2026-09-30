# Pagos con Mercado Pago en tu máquina

Guía para dejar funcionando el **pago digital con Mercado Pago (Checkout Pro)** en tu
computadora y probarlo de punta a punta: pedir, pagar con una tarjeta de prueba, volver a la app
y ver el pedido en el tablero de la barra.

> **Mercado Pago está en modo prueba**, y va a seguir así por mucho tiempo. Nada de lo que hagas
> con esta guía cobra plata real: las credenciales son de un vendedor de prueba, las tarjetas son
> de prueba y la app **no tiene producción activada**.

**Probado de punta a punta el 2026-09-30** con el botón oficial, el túnel y el webhook
configurados como dice esta guía (pedido `A-0268`: pagado, a la barra y entregado).

## En resumen

| Cuándo | Qué | Sección |
| --- | --- | --- |
| **Una sola vez** | Instalar `cloudflared` y cargar las tres credenciales con `user-secrets` | [1](#1-una-sola-vez-credenciales-de-prueba) |
| **Cada vez que probás** | Base → túnel → decirle a la API la URL del túnel → API → front | [2](#2-cada-vez-que-probás-levantar-todo-con-el-túnel) |
| Para pagar | Ventana de incógnito, comprador de prueba y tarjeta con titular `APRO` | [3](#3-pagar) |

Si sólo querés trabajar en otra cosa, **no hace falta nada de esto**: sin credenciales la app
anda igual y el pago digital contesta 503.

## Cómo funciona

1. En el checkout, cuando el nombre es válido, aparece el **botón oficial de Mercado Pago** (el
   *Wallet Brick* de MercadoPago.js, con su logo).
2. Al tocarlo se crea el pedido en `AwaitingPayment` (todavía **no** entra a la barra) y la API le
   pide a Mercado Pago un cobro (una *preferencia*) que vence en **15 minutos**. El botón lleva al
   cliente a la página de Mercado Pago.
3. El resultado llega por dos caminos, y cualquiera de los dos alcanza:
   - **La vuelta del cliente:** Mercado Pago lo devuelve a `/{boliche}/orders/{código}/{token}/payment`.
   - **El webhook:** Mercado Pago avisa a `/{boliche}/payments/notifications`, firmado.
4. La API consulta el pago **a Mercado Pago** (nunca le cree a la URL ni al aviso) y mueve el pedido:

   | Pago en Mercado Pago | Pedido |
   | --- | --- |
   | Aprobado | `Paid` → entra a la cola de la barra |
   | Rechazado o cancelado | `Canceled`, y los tragos vuelven al stock |
   | El cliente volvió sin pagar | `Canceled`, y los tragos vuelven al stock |
   | Pasaron 15 minutos sin pago | `Canceled`, y los tragos vuelven al stock |
   | Pendiente | Sigue esperando. Con el modo binario prácticamente no pasa |

**El efectivo comparte el estado `AwaitingPayment`** (lo espera la caja, US-24 a US-26), así que
todo lo de Mercado Pago mira además el método de pago:

- La **caja sólo ve y cobra pedidos en efectivo**. Uno que espera a Mercado Pago no aparece en su
  lista, y si lo buscan por código contesta que no hay nada que cobrar.
- El **vencimiento de 15 minutos es sólo del pago digital**. Un pedido en efectivo espera en la
  caja lo que tarde el cliente en llegar.
- En el checkout, con **efectivo** se ve nuestro botón *Confirmar pedido*; el botón de Mercado Pago
  sólo aparece con **pago digital**.

El diseño completo está en [sprint-2-backlog.md](sprints/sprint-2-backlog.md), US-24.

## Lo que necesitás

| Qué | De dónde sale |
| --- | --- |
| Lo de siempre: .NET, Node, pnpm y Docker | [README de e2e](../frontend/e2e/README.md#requisitos) |
| El **access token**, la **public key** y la **clave del webhook** de prueba | Pedíselos a Eugenia **por privado** (nunca por el grupo ni por un issue) |
| El **usuario y la contraseña del comprador de prueba** | Pedíselos a Eugenia por privado |
| `cloudflared` | `brew install cloudflared` (gratis, Apache-2.0) |

La cuenta de Mercado Pago es la de Eugenia. La app se llama **drinkit pruebas** (ID
`8240740196905075`). Los usuarios de prueba cuelgan de la app `3308656819990493`, que Mercado
Pago creó sola al generarlos. **No necesitás entrar al panel de Mercado Pago** para nada de esta
guía.

## 1. Una sola vez: credenciales de prueba

Las credenciales **no van en ningún archivo del repo**: ni en `appsettings.json`, ni en un
`appsettings.Development.json`, ni en un `.env`. Van en el almacén de secretos de desarrollo de
.NET (`dotnet user-secrets`), que vive fuera del proyecto (en `~/.microsoft/usersecrets/`) y que
la API lee sola cuando corre en Development.

Desde la raíz del repo:

```bash
dotnet user-secrets set "MercadoPago:AccessToken"   "APP_USR-…" --project backend/src/DrinkIt.Api
dotnet user-secrets set "MercadoPago:PublicKey"     "APP_USR-…" --project backend/src/DrinkIt.Api
dotnet user-secrets set "MercadoPago:WebhookSecret" "…"         --project backend/src/DrinkIt.Api
```

| Clave | Para qué | ¿Es secreta? |
| --- | --- | --- |
| `AccessToken` | Que la API cree cobros y consulte pagos | **Sí**: cobra en nombre de la cuenta |
| `PublicKey` | Que el celular dibuje el botón oficial. La API la sirve en `GET /payments/configuration`, sin caché, para no atarla al build de la PWA | No, pero va junto al resto |
| `WebhookSecret` | Verificar la firma de cada aviso de Mercado Pago | **Sí**. Sin ella, la API rechaza todos los avisos con 401 |

Para comprobar que quedaron, sin mostrarlas en pantalla:

```bash
dotnet user-secrets list --project backend/src/DrinkIt.Api | sed 's/=.*/= (cargado)/'
```

> El plugin de Mercado Pago de Claude Code **bloquea** escribir credenciales en un archivo del
> proyecto, aunque esté en `.gitignore`. No es un error: por eso se usa `user-secrets`.

## 2. Cada vez que probás: levantar todo con el túnel

Mercado Pago tiene que poder volver a tu app y avisarle a tu API, y tu máquina no está en
internet. El túnel le da una dirección pública HTTPS (el porqué completo está en
[Por qué hace falta un túnel](#por-qué-hace-falta-un-túnel)). **La dirección cambia cada vez que
abrís el túnel**, así que estos pasos se repiten en cada sesión, en este orden:

1. **La base**, como siempre:

   ```bash
   docker compose up -d
   ```

2. **El túnel**, en una terminal aparte que queda abierta:

   ```bash
   cloudflared tunnel --url http://localhost:4200
   ```

   Entre lo que imprime aparece una dirección tipo `https://algo-al-azar.trycloudflare.com`.

3. **Decile a la API cuál es.** Las dos claves apuntan al mismo túnel: la API se alcanza por
   `/api` gracias al proxy de `ng serve`.

   ```bash
   dotnet user-secrets set "MercadoPago:PublicAppUrl" "https://algo-al-azar.trycloudflare.com"     --project backend/src/DrinkIt.Api
   dotnet user-secrets set "MercadoPago:PublicApiUrl" "https://algo-al-azar.trycloudflare.com/api" --project backend/src/DrinkIt.Api
   ```

4. **La API**, con el perfil `https` (es el que usa el proxy del front). Si ya estaba corriendo,
   reiniciala: lee las URL al arrancar.

   ```bash
   dotnet run --project backend/src/DrinkIt.Api --launch-profile https
   ```

5. **El front**, como siempre. `angular.json` ya acepta las direcciones `*.trycloudflare.com` en
   desarrollo; cualquier otro host sigue bloqueado.

   ```bash
   pnpm --prefix frontend start
   ```

6. **Abrí la app por la dirección del túnel**, no por `localhost:4200`:
   `https://algo-al-azar.trycloudflare.com/bar-alfa/menu`.

Para comprobar que todo quedó bien antes de pagar, esto tiene que devolver tu public key:

```bash
curl https://algo-al-azar.trycloudflare.com/api/payments/configuration
```

### El webhook

**No tenés que registrar nada.** Cada cobro le manda a Mercado Pago su propia
`notification_url`, armada con la `PublicApiUrl` del paso 3 y el slug del boliche. Así, cada uno
recibe los avisos de sus propios pagos en su propio túnel.

En el panel de la app hay además una URL de webhook registrada: es un **respaldo**, y es de donde
sale la clave secreta. Apunta al túnel de la última persona que la configuró. No hace falta
tocarla para probar.

> **Sin verificar todavía:** la herramienta de historial de Mercado Pago no mostró ningún aviso
> enviado en modo prueba, aunque los pagos se aprobaron (por la vuelta del cliente). Para probar
> el webhook solo, pagá y **cerrá la pestaña sin volver a la app**: si el pedido pasa a `Paid`
> igual, llegó por el webhook. Si lo probás, anotá el resultado acá.

## 3. Pagar

1. Abrí la app **en una ventana de incógnito** (ver *Problemas frecuentes*) y pedí algo.
2. En el checkout escribí tu nombre y apellido. Recién con un nombre válido aparece el botón
   negro de Mercado Pago.
3. Tocalo, y en la página de Mercado Pago entrá con el **comprador de prueba**.
4. Pagá con una tarjeta de prueba. **El nombre del titular decide el resultado:**

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

## 4. Qué tenés que ver

| Pagaste con | En la app del cliente | En la barra (`/bar-alfa/staff/kds`) |
| --- | --- | --- |
| `APRO` | Vuelve sola y va al seguimiento: "Ya está pago y esperando en la barra" | El pedido aparece en **Nuevos**, en vivo |
| `OTHE`, `CONT` o `FUND` | "El pago no se completó y el pedido se canceló", con **Volver a intentar** (los tragos siguen en el carrito) | Nada; el stock vuelve a la carta |
| Volviste sin pagar | Igual que `OTHE` | Nada |

### El botón

- Mientras Mercado Pago lo dibuja (unos 200 ms), un rectángulo oscuro del mismo tamaño ocupa su
  lugar. Así no se ve el relleno gris claro que Mercado Pago muestra primero.
- Tiene el mismo tamaño que nuestro botón dorado, así la pantalla no salta al cambiar.
- **Si no hay public key, o el script de Mercado Pago no carga** (un bloqueador de anuncios, el
  wifi filtrado del boliche), aparece nuestro botón dorado *Pagar con Mercado Pago*. Hace lo mismo
  con una redirección común: el pago nunca se queda sin botón.

## Cómo va configurado el cobro

Así lo pidió la revisión de calidad de Mercado Pago (`/mp-review`):

- **Modo binario:** el pago se aprueba o se rechaza en el momento; nunca queda pendiente.
- **Sin Rapipago, Pago Fácil ni transferencia por cajero** (`ticket` y `atm`): se pagan horas
  después, y un pedido espera 15 minutos.
- **En una sola cuota.**
- Van el **nombre y apellido** del cliente, el **id y la descripción** de cada trago (con su
  nota) y **DRINKIT** como texto del resumen de la tarjeta.
- El **logo oficial** va en el botón (Wallet Brick con el tema `dark`).

### Lo que la revisión marca y no hacemos, a propósito

| Práctica | Por qué no |
| --- | --- |
| Email, documento, teléfono y dirección del comprador | La app no los pide: en la barra alcanza con el nombre, y pedir más frena la compra |
| Categoría de cada ítem (`category_id`) | La lista oficial de Mercado Pago no tiene comida ni bebida; la única que aplicaría es `others`, que no suma información |
| Devoluciones por la API (`refunds`) | Un pago que llega después de cancelado se avisa al cliente y se devuelve fuera del sistema (backlog, US-24) |
| Contracargos, reportes de liquidación y de dinero liberado | Son de operación del boliche, no de la app |
| Checkout en modal | El botón redirige en la misma pestaña, que en el celular funciona mejor |

## Tests que usan Mercado Pago

| Qué | Comando | Sin token |
| --- | --- | --- |
| Todo el backend | `dotnet test backend/DrinkIt.slnx` | Los de Mercado Pago se saltean solos |
| Sólo el contrato con Mercado Pago | `dotnet test backend/tests/DrinkIt.Api.IntegrationTests --filter "FullyQualifiedName~MercadoPagoContractTests"` | Se saltean |

El **contrato** (`MercadoPagoContractTests`) le habla a la API real de Mercado Pago con tu token
de prueba: crea preferencias de verdad y las lee de vuelta. Es lo que detecta si alguna vez
rompemos cómo le hablamos a Mercado Pago.

Los tests del front **no** cargan el script de Mercado Pago: el botón se dibuja con un reemplazo.
Por eso lo que depende del script real (el tema, el tamaño, el relleno que se ve mientras carga)
se verificó a mano en un navegador.

### Lo que Mercado Pago hace distinto de lo que dice su documentación

Salió de los tests de contrato y de las pruebas en el navegador:

- **Descarta las URL de vuelta `http://localhost`** sin avisar.
- **No deduplica preferencias:** pedir dos veces el cobro del mismo pedido abre dos. Por eso el
  pedido guarda su checkout y un reintento devuelve ése.
- **El tema oscuro del botón es `dark`, no `black`**, aunque una tabla de la documentación diga
  `black`. Con `black` el botón no se dibuja y aparece el nuestro.
- **El botón tiene 4 px de margen arriba y abajo**, y mientras carga muestra un relleno gris claro
  más alto que el botón. El componente compensa las dos cosas.
- **Avisa que está listo (`onReady`) unos 10 ms antes de que el botón reemplace ese relleno.** Por
  eso el componente espera a que aparezca el `<button>` de verdad (y, por las dudas, lo muestra
  igual al segundo). Se midió grabando cada cuadro que pinta el navegador: con capturas cada 40 ms
  el destello no se ve.
- **Mientras espera nuestra respuesta, el botón dice "Redirigiendo"** con una barra que se llena,
  y si el pedido se rechaza vuelve solo a su estado normal: no hace falta redibujarlo.

## Por qué hace falta un túnel

Mercado Pago está en internet y tu máquina no. Hay dos cosas que necesitan llegar de Mercado Pago
a vos:

- **La vuelta del cliente.** Mercado Pago **ignora** cualquier URL de vuelta que no sea HTTPS
  pública (lo verifica el test `StartCheckoutAsync_WithALocalReturnAddress_MercadoPagoDropsIt`).
  Sin túnel, después de pagar el cliente se queda en la pantalla de Mercado Pago.
- **El webhook.** Mercado Pago tiene que poder llamar a tu API. Sin dirección pública, la API ni
  siquiera se la pasa (`notification_url` queda vacío).

En Azure la API y la PWA tienen direcciones públicas HTTPS, así que ahí no hace falta ningún
túnel. Fuera de Development, la API **no arranca** si hay token pero falta una dirección pública
HTTPS o la clave del webhook, y el error dice qué falta.

## Problemas frecuentes

| Qué pasa | Por qué | Qué hacer |
| --- | --- | --- |
| Al pagar dice **503** / "Mercado Pago no respondió" | No hay token cargado, o está mal | Paso 1, y reiniciá la API |
| Aparece el botón **dorado** y no el de Mercado Pago | Falta la public key, el script de Mercado Pago no cargó, o todavía no escribiste un nombre válido | Probá el `curl` del paso 2. Si devuelve `null`, paso 1 y reiniciá la API. Si no, mirá la consola del navegador |
| Pagaste y **no volviste a la app** | Abriste la app por `localhost` o la API no tiene la URL del túnel | Paso 2 completo, y abrí la app por el túnel |
| El pedido pagado **se canceló solo** | Pasaron 15 minutos sin que llegara el resultado | Paso 2 |
| Cerraste el túnel y abriste otro, y dejó de volver | La URL cambió y la API tiene la vieja | Pasos 3 y 4 de la sección 2 |
| Mercado Pago **no te deja pagar** o da error al confirmar | Estás logueado en Mercado Pago con otra cuenta (por ejemplo la del vendedor) en el mismo navegador | Pagá en una ventana de incógnito, entrando con el comprador de prueba |
| El webhook contesta **401** | Falta la clave secreta o no coincide | Paso 1 |
| Por el túnel aparece **"Blocked request"** (403) | El túnel no es de `trycloudflare.com` | Usá `cloudflared`. El flag `--allowed-hosts` del CLI es sólo `true`/`false` y `true` abre todo: no lo uses |
| La API no arranca: **"MercadoPago:… must be…"** | Estás corriendo fuera de Development con la configuración incompleta | Corré en Development, o completá lo que dice el error |
| Los e2e de Playwright **fallan al pagar** | Con token, el checkout va a Mercado Pago de verdad | Ver la nota de abajo |

> **Nota sobre los e2e.** Los tests de Playwright que crean pedidos (checkout, seguimiento, KDS,
> escaneo) pagan por la interfaz. Con el pago real, esos pedidos pasan por la página de Mercado
> Pago. Cómo resolverlo (una pasarela simulada sólo para esos tests, y un e2e aparte que pague de
> verdad) está en discusión en US-24.

## Seguridad

- **Nunca** pongas el token ni la clave del webhook en un archivo del repo, en un commit, en un
  issue ni en el chat del grupo.
- Si un token se filtra, se renueva desde el panel de Mercado Pago y se avisa al equipo.
- Producción **no está activada** en la app. Antes de activarla hay que pasar `/mp-review`, el
  formulario de homologación de Mercado Pago y cargar las credenciales en Azure.
