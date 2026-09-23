# Backlog del Sprint 2

La [consigna del Sprint 2](sprint-2.md) define **cómo se escribe una story**, no qué se
construye: no trae una lista de puntos funcionales como la del Sprint 1. Así que el formato
de acá abajo es obligatorio, y **el alcance es decisión nuestra** — sale de lo que el Sprint
1 dejó fuera a propósito y del diseño funcional ([drink.it.v2.md](../drink.it.v2.md)).

Lo que quedó del [backlog del Sprint 1](sprint-1-backlog.md) sigue valiendo: cómo escribimos
las stories, las ocho reglas de los criterios de aceptación y los dos formatos
(Dado/Cuando/Entonces para lo que tiene disparador, lista de reglas para lo que vale
siempre). Acá no se repiten.

**Lo que cambia es el tamaño, y lo pide el enunciado:** cada story lleva **entre 2 y 4
criterios**, y un bloque de **notas** con dependencias, necesidades de diseño y casos borde.
En el Sprint 1 varias llegaron a seis criterios y lo que pasó fue que una story tardaba
demasiado en dar verde. Si una historia necesita cinco, son dos historias.

## Dónde quedamos

El Sprint 1 dejó el recorrido del cliente entero hasta "esperando": se entra al local por el
QR, se lee la carta, se arma el pedido con notas, se confirma y se lo sigue en una pantalla
pública. Después de la entrega cerraron US-05, US-07 y US-08, así que los dos ABM están
completos y de la carta sólo falta agruparla por categoría.

Del otro lado del mostrador no hay nadie: **el rol KDS existe pero no tiene pantalla**, así
que un pedido confirmado se queda en la cola para siempre. Pagar es confirmar, el aviso de
"listo" es una consulta cada tres segundos, y no existe la caja. Y en toda la base **no hay
una sola columna que diga quién hizo qué ni cuándo**.

El Sprint 2 es, entonces, **la otra mitad del mostrador**: que el pedido se prepare, se
entregue y el cliente se entere sin mirar la pantalla.

## Quiénes usan el sistema en este sprint

| Rol               | Qué hace                                                               | Se autentica |
| ----------------- | ---------------------------------------------------------------------- | ------------ |
| **Cliente**       | Pide, paga, recibe el aviso y retira mostrando su QR                   | No           |
| **KDS**           | La cuenta de la estación de barra. **Este sprint estrena sus pantallas** | Sí           |
| **Cajero**        | Cobra en efectivo. **Rol nuevo**: hoy no existe en el sistema          | Sí           |
| **Administrador** | Lo de siempre                                                          | Sí           |
| **Mozo**          | Se le puede crear la cuenta. Sigue sin pantalla: la entrega en mesa llega con el VIP | Sí |

## Resumen

**Arrastre del Sprint 1** — queda una sola. US-05, US-07 y US-08 cerraron después de la
entrega, así que el ABM de la carta y el del personal están completos: se carga, se corrige,
se marca agotado, se da de baja y nada se borra.

| ID    | Story                          | Depende de   | Estado      |
| ----- | ------------------------------ | ------------ | ----------- |
| US-14 | Agrupar la carta por categoría | US-06, US-09 | Sin empezar |

**Transversal** — toca todas las tablas, así que va antes que las que crean tablas nuevas.

| ID    | Story                            | Depende de |
| ----- | -------------------------------- | ---------- |
| US-30 | Saber quién tocó qué y cuándo    | —          |

**Núcleo del Sprint 2** — el pedido se prepara y se entrega.

| ID    | Story                                | Depende de   |
| ----- | ------------------------------------ | ------------ |
| US-15 | Ver la cola de la barra              | US-11        |
| US-16 | Tomar un pedido y sacarlo de la cola | US-15        |
| US-18 | Marcar el pedido como listo          | US-16        |
| US-19 | Entregar el pedido en la barra       | US-18, US-20 |
| US-20 | Que el cliente vea su QR de retiro   | US-12        |
| US-21 | Que me avise el celular              | US-18        |
| US-22 | Que el estado se actualice solo      | US-12, US-18 |
| US-23 | Cancelar un pedido                   | US-15        |

**Carril del pago** — dejar de simular que confirmar es pagar.

| ID    | Story                          | Depende de   |
| ----- | ------------------------------ | ------------ |
| US-24 | Elegir cómo pagar              | US-11        |
| US-25 | Pagar en efectivo en la caja   | US-24        |
| US-26 | Cobrar un pedido en la caja    | US-25        |

---

## Transversal

### US-30 · Saber quién tocó qué y cuándo

> **Como** administrador del local
> **quiero** que cada dato guarde quién lo creó o lo modificó por última vez, y cuándo
> **para** reconstruir qué pasó cuando un precio aparece cambiado o alguien reclama un pedido
> de hace dos horas.

**Criterios de aceptación**

1. **Dado** que di de alta a alguien del equipo o cargué un trago, **cuando** abro su ficha,
   **entonces** veo quién lo creó y en qué fecha y hora.
2. **Dado** que otro administrador cambió el precio de un trago, **cuando** miro la ficha del
   trago, **entonces** veo quién fue el último en tocarlo y cuándo.
3. **Dado** un pedido de esta noche, **cuando** lo miro desde la administración, **entonces**
   veo las marcas de su recorrido: cuándo se confirmó, cuándo quedó listo y cuándo se
   entregó.
4. **Dado** un dato cargado antes de que esto existiera, **cuando** lo miro, **entonces** el
   sistema dice que no hay registro, en vez de mostrar una fecha inventada.

**Notas**

- **Depende de** nada, y por eso mismo **va primero**: toca todas las tablas, y este sprint
  suma las del pago. Hecha al final, la migración es el doble de grande y hay que acordarse
  de cada tabla nueva.
- **El modelo de datos ya lo promete y la base no lo cumple.**
  [modelo-de-datos.md](../modelo-de-datos.md) declara `created_at` en `VENUE`, `STAFF_USER`,
  `CUSTOMER`, `PRODUCT` y `ORDER_ITEM`, y `confirmed_at`, `ready_at`, `delivered_at` y
  `prepared_by_staff_user_id` en `CUSTOMER_ORDER`. En el código **no existe ninguno**: la
  única marca de tiempo real es `PaidAt` en `Order`. Esta story cierra esa brecha, y de paso
  deja de ser mentira el diagrama que le mostramos a la cátedra.
- **Diseño, y no es opcional:** hay que decidir **dónde se ve cada dato** antes de
  construirlo. Un campo que se guarda pero no se muestra en ninguna pantalla no es una story
  —es trabajo técnico sin forma de aceptarlo—, y lo que no se ve tampoco se prueba. Lo mínimo
  es el pie de la ficha de personal, el de la ficha de producto y la ficha de pedido.
- **Borde, el que hace ruido:** las filas que ya existen. Una migración que le pone la fecha
  de hoy a todo miente sobre cuándo pasaron las cosas, que es justo lo que esta story viene a
  evitar. Van nulas, y el criterio 4 es el que lo obliga.
- **Borde: en un pedido del cliente no hay "quién".** El cliente no tiene cuenta, así que ahí
  el autor es el sistema. No inventar un usuario de mentira para llenar la columna.
- **Borde: si cada caso de uso escribe su propia fecha, alguno se la va a olvidar**, y nadie
  se entera hasta la noche en que hace falta el dato. Conviene que la marca se ponga en un
  solo lugar.
- **A favor:** el reloj ya está inyectado (`TimeProvider`, el que usa la estrategia de pago),
  así que las fechas se pueden fijar en un test en vez de depender de la hora de la máquina.

---

## La barra

### US-15 · Ver la cola de la barra

> **Como** estación de barra
> **quiero** ver en la tablet los pedidos pagados ordenados del que más esperó al que menos
> **para** atender primero a quien hace más rato que espera, sin preguntarle a nadie.

**Criterios de aceptación**

1. **Dado** que hay pedidos dando vueltas, **cuando** abro la pantalla de la barra,
   **entonces** los veo repartidos en **Nuevos**, **En preparación** y **Listos**, y dentro
   de cada columna del más viejo al más nuevo, con su número, quién lo pidió, si es barra o
   mesa, sus tragos con cantidad y nota, y hace cuánto espera.
2. **Dado** un pedido que espera hace **menos de 5 minutos**, **cuando** lo miro, **entonces**
   se ve normal; **a los 5** pasa a ámbar y **a los 10** pasa a rojo y dice "urgente", sin
   que haga falta leer el reloj de cada tarjeta.
3. **Dado** que entré con una cuenta que no es de barra, **cuando** abro esa dirección,
   **entonces** el sistema no me la muestra.
4. **Dado** que no hay ningún pedido esperando, **cuando** miro la pantalla, **entonces** me
   dice que la cola está vacía, en vez de quedar en blanco.

**Notas**

- **Depende de** US-11 (hoy ya hay pedidos en estado `Queued`).
- **Diseño: ya está dibujado**, en alta fidelidad — `design/wireframes/KdsBoard.dc.html`, con
  `KdsSeleccion`, `KdsDetalle` y `KdsEscanear` al lado. Tablet apaisada, tres columnas, fondo
  oscuro y números grandes para leer a un metro. No hay nada que decidir de diseño.
- **El umbral salió del wireframe y por eso está en el criterio 2**, no acá: las tarjetas
  dibujadas dicen "11 min · urgente" en rojo, 6 minutos en ámbar y 2 minutos en gris. Un
  umbral que vive en una nota es uno que nadie prueba.
- **Borde de multi-tenancy:** el local sale del token de la estación, **nunca** de la URL.
  Esta pantalla necesita su test de aislamiento igual que el resto.
- **Borde:** la antigüedad se cuenta **desde que se pagó**, no desde que se armó el carrito
  — si no, un carrito abandonado a las 23:00 entra a la cola en rojo.

### US-16 · Tomar un pedido y sacarlo de la cola

> **Como** estación de barra
> **quiero** tomar uno o varios pedidos y que desaparezcan de la cola de nuevos
> **para** que la barra de al lado no prepare el mismo trago dos veces.

**Criterios de aceptación**

1. **Dado** un pedido en la cola, **cuando** lo tomo, **entonces** pasa a la columna de
   preparación con su ticket a la vista y deja de aparecer entre los nuevos.
2. **Dado** que elegí varios pedidos para prepararlos juntos, **cuando** los tomo,
   **entonces** cada uno queda tomado por separado, con su propio número y su propio ticket,
   y no sale uno combinado.
3. **Dado** que dos estaciones tienen la misma cola abierta, **cuando** las dos toman el
   mismo pedido casi a la vez, **entonces** una sola lo toma y la otra recibe un aviso de
   que ya se lo llevaron.
4. **Dado** que tomé un pedido por error, **cuando** uso "Devolver a la cola", **entonces**
   vuelve a los nuevos conservando su antigüedad original.

**Notas**

- **Depende de** US-15.
- **Acá adentro está el ticket, y era US-17.** La separamos y no tenía sentido: tomar e
  imprimir son **el mismo gesto** (§11 del diseño funcional, y el wireframe lo dibuja como un
  solo botón "Imprimir" en la tarjeta). Dos stories para un botón son dos stories que se
  pisan.
- **El ticket no se imprime en este sprint: se muestra en pantalla**, con su número, sus
  tragos con nota, el tipo de entrega y **su QR de verdad**, el que US-18 escanea. La
  impresora térmica con el QR en ESC/POS —lo que dice el wireframe `KdsDetalle`— es
  **deuda para un refactor**, y necesita hardware: cuando llegue no cambia nada de esta
  story, cambia quién recibe el mismo documento.
- **Dependencia aprobada:** `angularx-qrcode` para dibujar el QR (ver _Dependencias nuevas_).
- **Diseño:** `KdsSeleccion.dc.html` tiene el multi-selección dibujado, con la barra de abajo
  que sugiere agrupar ("2 pedidos con Gin Tonic entre los próximos — preparalos juntos") y el
  "Se imprimió #123 · Devolver a la cola" del criterio 4.
- **Borde, el importante:** el criterio 3 es una carrera real, dos tablets en la misma barra.
  Se resuelve en el dominio con la transición, no con un `if` en la pantalla.
- Se permite elegir cualquiera de los próximos de la cola, no sólo el primero: agrupar
  pedidos del mismo trago es más rápido en total (§11).

### US-18 · Marcar el pedido como listo

> **Como** estación de barra
> **quiero** marcar que el trago ya está hecho, de la forma que tenga a mano
> **para** que al cliente le llegue el aviso sin que yo pelee con la tablet.

**Criterios de aceptación**

1. **Dado** un pedido en preparación, **cuando** toco "Listo" en su tarjeta, **entonces**
   pasa a la columna de listos y se le avisa al cliente.
2. **Dado** que tengo el ticket en la mano, **cuando** escaneo su QR con la cámara,
   **entonces** queda listo sin que tenga que buscarlo entre las tarjetas.
3. **Dado** que la cámara no está —sin permiso, sin luz, ticket mojado—, **cuando** busco el
   pedido por su número o por el nombre del cliente, **entonces** lo marco listo igual.
4. **Dado** que escaneo o escribo un código que no es de ningún pedido de mi local, **o** uno
   de un pedido ya entregado o cancelado, **entonces** el sistema me lo dice y no cambia
   nada.

**Notas**

- **Depende de** US-16.
- **Son tres caminos al mismo lugar, y se construyen en este orden:** primero el botón, que
  no necesita nada y deja la story cerrable; después la búsqueda por código o nombre; el
  escaneo último. Si la cámara da problemas, se recorta el criterio 2 y la story igual cierra
  — al revés, se cae entera.
- **El escaneo se puede hacer hoy y sin comprar el lector.** `BarcodeDetector` es una API
  nativa del navegador en Chrome/Android, que es lo que corre una tablet de barra. Safari,
  iOS y Firefox no la tienen, y ahí entra el polyfill (ver _Dependencias nuevas_). Es la
  misma API en los dos casos: el código se escribe una vez.
- **Escribirlo como si el lector ya existiera:** lo que avanza el pedido es "este código pasa
  a la siguiente etapa", no "el botón de esta tarjeta". La cámara es una forma de tipear el
  código; el lector USB —que funciona como teclado y manda Enter— es otra; el campo manual es
  la tercera. Con el caso de uso recibiendo un código, **los tests y el E2E le pasan el
  código y nunca necesitan una cámara**, y el día que se compre el lector no se toca nada.
- El criterio 3 ya está en el wireframe ("¿El lector no lo toma? Buscá el pedido a mano — por
  número o por el nombre del cliente"). En un boliche la cámara falla seguido.
- **Borde de la cámara:** la tablet está montada en un soporte, así que acercarle el ticket
  es incómodo. Es un argumento para comprar el lector, no para no hacer la story — y es
  exactamente por qué el criterio 1 existe.
- Marcar listo es lo que dispara el aviso al cliente (US-21). El dominio publica el evento;
  nadie llama al servicio de push desde la barra.

### US-19 · Entregar el pedido en la barra

> **Como** estación de barra
> **quiero** marcar que ya le di el pedido al cliente
> **para** cerrarlo y que la lista de listos muestre sólo lo que falta entregar.

**Criterios de aceptación**

1. **Dado** un pedido listo, **cuando** escaneo el QR que el cliente muestra en su celular,
   **entonces** queda entregado y sale de la lista.
2. **Dado** que el celular del cliente está sin batería o con la pantalla rota, **cuando**
   busco su pedido por número o por su nombre y toco "Entregado", **entonces** lo entrego
   igual.
3. **Dado** un pedido que todavía no está listo, **cuando** alguien lo escanea, **entonces**
   el sistema avisa que no está listo y no lo entrega.
4. **Dado** un pedido ya entregado, **cuando** se vuelve a escanear, **entonces** el sistema
   avisa que ya se entregó — es el caso de dos personas reclamando el mismo número.

**Notas**

- **Depende de** US-18 y US-20.
- **Es el mismo campo de escaneo y los mismos tres caminos que US-18**, y por eso esta story
  es chica: el wireframe `KdsEscanear.dc.html` dibuja **uno solo** que no pregunta nada — lee
  el código, mira en qué estado está el pedido y lo avanza. Ticket de barra → listo. QR del
  cliente → entregado.
- **Acá el escaneo importa más que en US-18**, porque es lo único que verifica que el pedido
  es de quien lo reclama. Por eso el botón del criterio 2 va detrás de buscar el pedido, y no
  suelto en la tarjeta: obliga a mirar el número antes de entregar.
- **Borde del criterio 2:** quien sepa un número ajeno podría reclamarlo. Es aceptable porque
  hay una persona mirando, y es justamente por eso que el camino principal es el QR.

### US-23 · Cancelar un pedido

> **Como** estación de barra
> **quiero** cancelar un pedido que no se va a preparar
> **para** que la cola muestre lo que de verdad hay que hacer.

**Criterios de aceptación**

1. **Dado** un pedido en la cola o en preparación, **cuando** lo cancelo indicando el motivo,
   **entonces** queda cancelado y el cliente lo ve en su pantalla de seguimiento.
2. **Dado** un pedido ya entregado, **cuando** intento cancelarlo, **entonces** el sistema no
   me deja.
3. **Dado** que cancelo un pedido pagado, **cuando** lo confirmo, **entonces** el sistema deja
   registrado que hay plata a devolver, aunque la devolución se haga fuera del sistema.

**Notas**

- **Depende de** US-15.
- El diseño funcional deja las reglas de cancelación **explícitamente sin definir** (§5). Lo
  de arriba es una propuesta mínima: hay que confirmarla antes de construirla.
- **Borde:** qué pasa con un pedido que nadie retira en toda la noche. Proponemos **no**
  cancelarlo solo en este sprint — un vencimiento automático necesita su propia discusión.

## El cliente

### US-20 · Que el cliente vea su QR de retiro

> **Como** cliente que ya pagó
> **quiero** tener mi código a la vista en la pantalla de seguimiento
> **para** mostrarlo en la barra y llevarme mi trago sin discutir con nadie.

**Criterios de aceptación**

1. **Dado** que mi pedido está pagado, **cuando** abro la pantalla de seguimiento, **entonces**
   veo mi QR junto al número de pedido, los dos legibles.
2. **Dado** que tengo el brillo del celular al mínimo, **cuando** muestro el QR en la barra,
   **entonces** la tablet lo lee igual.
3. **Dado** que mi pedido fue entregado, **cuando** miro la pantalla, **entonces** el QR ya no
   sirve para reclamar otra vez.

**Notas**

- **Depende de** US-12.
- **Diseño:** el QR va con fondo claro aunque la app esté en oscuro, y grande. El criterio 2
  no es decorativo: un QR chico sobre fondo negro y con poco brillo no se lee. Se prueba
  contra la pantalla de US-19, no en el navegador.
- Es el mismo identificador del ticket (§10), mostrado en otro soporte — no son dos códigos.
- **Dependencia aprobada:** `angularx-qrcode` (ver _Dependencias nuevas_).

### US-21 · Que me avise el celular

> **Como** cliente que está lejos de la barra
> **quiero** que me llegue un aviso al celular cuando mi pedido está listo
> **para** no tener que mirar la pantalla cada dos minutos.

**Criterios de aceptación**

1. **Dado** que acabo de pagar, **cuando** termina el pago, **entonces** la app me ofrece
   activar los avisos, explicando para qué sirve, y puedo decir que no.
2. **Dado** que acepté los avisos, **cuando** la barra marca mi pedido listo, **entonces** me
   llega la notificación con mi número, aunque tenga la app cerrada.
3. **Dado** que rechacé los avisos o mi teléfono no los soporta, **cuando** mi pedido queda
   listo, **entonces** igual lo veo en la pantalla de seguimiento sin refrescar.

**Notas**

- **Depende de** US-18.
- **El momento importa** (§9.1): el permiso se pide **después de pagar**, que es cuando el
  cliente quiere que le avisen. Pedirlo al entrar lo hace rechazar.
- **Borde conocido y aceptado** (§9.2): en iOS el push sólo funciona si la PWA se agregó a la
  pantalla de inicio. Por eso el criterio 3 no es un plan B opcional — es el camino de una
  parte grande de los clientes.
- La suscripción se ata al **pedido**, no a una cuenta: el cliente no tiene login.
- **Dependencia técnica:** hacen falta claves VAPID y guardar la suscripción. Nada de
  proveedores pagos.

### US-22 · Que el estado se actualice solo

> **Como** cliente mirando mi pedido
> **quiero** que la pantalla cambie sola cuando cambia el estado
> **para** enterarme en el momento y no cuando se me ocurre recargar.

**Criterios de aceptación**

1. **Dado** que tengo la pantalla de seguimiento abierta, **cuando** la barra cambia el estado
   de mi pedido, **entonces** lo veo reflejado en menos de dos segundos sin tocar nada.
2. **Dado** que me quedé sin señal un rato, **cuando** vuelve la conexión, **entonces** la
   pantalla se pone al día sola y me avisa si estuvo desconectada.
3. **Dado** que mi pedido fue entregado o cancelado, **cuando** eso pasa, **entonces** la
   pantalla deja de consultar.

**Notas**

- **Depende de** US-12 y US-18.
- Hoy esto funciona **consultando cada tres segundos** y estaba anotado como deuda desde el
  Sprint 1. Esta story es reemplazarlo por tiempo real (§9.3).
- El criterio 3 **ya está hecho** (`fix(tracking)`, 2026-09-19) y el aviso de falta de señal
  del criterio 2 también. Lo que queda es el criterio 1.
- **Borde de escala:** una tablet de barra y cien celulares mirando el mismo local. Vale la
  pena medir antes de dar por buena la solución.

## El pago

### US-24 · Elegir cómo pagar

> **Como** cliente que confirma su pedido
> **quiero** elegir si pago desde el celular o en la caja
> **para** poder pedir aunque no tenga la tarjeta a mano.

**Criterios de aceptación**

1. **Dado** que confirmo mi pedido, **cuando** llego al pago, **entonces** puedo elegir entre
   los métodos que el local acepta, con el total a la vista.
2. **Dado** que elijo pago digital, **cuando** el pago se confirma, **entonces** mi pedido
   entra en la cola de la barra sin que nadie más intervenga.
3. **Dado** que elijo efectivo, **cuando** confirmo, **entonces** mi pedido queda esperando el
   cobro en caja y **no** aparece en la barra.

**Notas**

- **Depende de** US-11.
- **Ya está la mitad hecha:** existen `PaymentMethod` con los tres valores y el puerto
  `IPaymentStrategy`, con la estrategia digital implementada. Agregar efectivo es agregar una
  clase, no tocar el flujo.
- **El pago digital se sigue simulando.** El enunciado del Sprint 2 no fija alcance, así que
  la decisión es nuestra y la sostenemos: la del Sprint 1 permitía simularlo y una pasarela
  real es otra conversación —webhook, entorno de prueba y qué pasa si el pago queda a medias—
  que no entra en el mismo sprint que la barra.
- **Borde:** qué pasa si el cliente abandona la pantalla de pago. Proponemos que el pedido
  siga siendo un carrito editable hasta que se confirme el pago.

### US-25 · Pagar en efectivo en la caja

> **Como** cliente que paga en efectivo
> **quiero** que la app me muestre el código que tengo que dar en la caja
> **para** que el cajero cobre y listo, sin dictarle mi pedido trago por trago.

**Criterios de aceptación**

1. **Dado** que elegí efectivo, **cuando** confirmo, **entonces** veo mi código bien grande y
   la indicación de acercarme a la caja.
2. **Dado** que mi pedido espera el cobro, **cuando** miro la pantalla de seguimiento,
   **entonces** distingo "esperando que pagues" de "esperando que lo preparen".
3. **Dado** que el cajero cobró, **cuando** eso pasa, **entonces** mi pantalla cambia sola y
   el pedido entra en la cola de la barra.

**Notas**

- **Depende de** US-24.
- El estado `AwaitingPayment` ya existe en el dominio, declarado y sin transiciones. Esta
  story es la que las escribe.
- **Borde:** un pedido que nadie va a pagar nunca ocupa la caja. Mismo caso que en US-23:
  proponemos no vencerlo automáticamente todavía, pero que el cajero lo pueda cancelar.
- **Diseño:** el código tiene que leerse de lejos y en penumbra; es lo mismo que resuelve el
  QR de US-20 y conviene que sea la misma pantalla.

### US-26 · Cobrar un pedido en la caja

> **Como** cajero
> **quiero** buscar el pedido por su código y confirmar que cobré
> **para** despachar la fila rápido sin armar pedidos yo.

**Criterios de aceptación**

1. **Dado** que un cliente me da su código, **cuando** lo busco, **entonces** veo su pedido
   con los tragos y el total a cobrar.
2. **Dado** que tengo el pedido en pantalla, **cuando** confirmo el cobro, **entonces** pasa a
   la cola de la barra y desaparece de mi lista de pendientes.
3. **Dado** un código que no existe o que ya se cobró, **cuando** lo busco, **entonces** el
   sistema me lo dice con claridad.

**Notas**

- **Depende de** US-25.
- **Dependencia sin tarjeta: el rol `Cashier` no existe.** Hoy `StaffRole` tiene
  Administrator, Kds y Waiter. Agregarlo toca el alta de personal, el ingreso y la
  autorización. Va adentro de esta story, y conviene hacerla primero por eso.
- **Diseño: ya está dibujado** — `CajeroLogin`, `CajeroBuscar` y `CajeroConfirmar` en
  `design/wireframes/`. (`CajeroVip` es de la coordinación de mesas: no va en este sprint.)
- **Borde:** el cajero **no arma pedidos** (§12). Si el cliente quiere agregar algo, vuelve a
  pedir desde el celular. Eso es una decisión del diseño, no una limitación a arreglar.

## Qué proponemos comprometer

Dos carriles y una story que va antes que los dos.

**Va primero, y sola: US-30.** Los campos de auditoría tocan todas las tablas que hoy
existen, y este sprint crea más. Hecha al final, la migración es el doble y alguna tabla
nueva se queda sin las columnas. Es chica si se hace ahora y molesta si se hace después.

**Va sí o sí: la barra.** US-15 a US-19 más US-20, US-21 y US-22. Es el recorrido que hoy
está cortado: sin esto un pedido confirmado no se prepara nunca. Es el sprint.

**Va segundo, y sólo si la barra ya está cerrada: el pago.** US-24, US-25 y US-26. Ojo con
el tamaño real: trae el rol `Cashier`, que toca autenticación y el ABM de personal. Es el
primer candidato a recortar si la barra se estira.

**Fuera, y ni siquiera escrito: el sector VIP.** Mesas, saldo con concurrencia sobre plata,
una pantalla nueva para un rol nuevo y una pregunta de diseño sin responder (§8.1) son un
sprint entero por sí solas. Meterlas junto con la barra es cómo se llega a la entrega con
dos carriles a medias en vez de uno terminado. Cuando le toque, se escriben desde cero
contra el §8 del diseño funcional.

## Lo que hay que resolver antes de empezar

1. **Confirmar el alcance entre nosotros.** El enunciado no lo fija, así que la lista de
   arriba es nuestra y hay que aprobarla antes de abrir la primera rama.
2. **Dónde se muestra cada dato de auditoría** (US-30). Sin un lugar donde verlo no hay
   criterio que se pueda aceptar apretando botones.
3. **Si el pago se sigue simulando.** Cambia el tamaño de todo el carril del pago.

Lo que **ya no** está en esta lista y estaba: los diseños. Todas las pantallas de este
sprint —KDS, caja, cancelación, efectivo— **están dibujadas en alta fidelidad** en
`design/wireframes/`. No hay ninguna decisión de diseño pendiente.

## Deuda que arrastramos

Está toda en el Sprint 1 y sigue igual: los tests de punta a punta no corren en la CI, el
prefijo `/api` fuera de desarrollo, la cuenta de Storage para las fotos, y los tres lugares
que nombran el almacenamiento del navegador. Nada de eso cambió y nada de eso traba este
sprint, pero la de la CI empieza a doler cuando el recorrido tiene cuatro actores.

**Nueva, y de este sprint: la impresora térmica.** El ticket sale por pantalla y no por
papel. Imprimirlo de verdad con el QR en ESC/POS necesita comprar la impresora, así que no
es una decisión de software. Cuando llegue, el documento ya existe: cambia quién lo recibe.

## Dependencias nuevas

Aprobadas el **2026-09-23**. Las tres son **MIT** y están vivas — se verificó contra el
registro de npm el mismo día, no contra lo que decía un tutorial.

| Paquete            | Versión | Licencia | Última publicación | Para qué                                     |
| ------------------ | ------- | -------- | ------------------ | -------------------------------------------- |
| `angularx-qrcode`  | 22.0.1  | MIT      | 2026-07-24         | Dibujar el QR del cliente y el del ticket    |
| `barcode-detector` | 3.2.2   | MIT      | 2026-08-16         | Leerlo donde el navegador no sabe            |
| `zxing-wasm`       | 3.1.4   | MIT      | 2026-09-10         | Lo trae `barcode-detector`, no se instala solo |

**Leer el QR casi no es una dependencia.** `BarcodeDetector` es una **API nativa del
navegador**: en Chrome sobre Android —que es lo que corre la tablet de la barra— se lee con
la cámara sin instalar nada. Safari, todo iOS y Firefox no la implementan, y para eso está
`barcode-detector`, que **no es otra librería con otra API: es un polyfill de esa misma
API** con ZXing compilado a WebAssembly. Se escribe el código una vez contra el estándar y
el polyfill se carga sólo donde falta.

Importa para el cajero si atiende desde su propio celular, y para cualquier tablet que no
sea Android: si es Apple, se baja el WASM. Funciona, pero no es gratis en bytes y conviene
cargarlo sólo cuando hace falta.

**Descartada:** `qr-scanner` (nimiq), MIT pero **sin publicar desde 2022**. Y `@zxing/library`
está en modo mantenimiento declarado por sus propios autores — `zxing-wasm` es el sucesor
vivo. No entra nada de Scanbot ni STRICH: son comerciales.

**Instalar cuando arranque la story, no ahora.** Hoy quedan aprobadas y escritas.
