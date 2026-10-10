# Backlog del Sprint 3

La [consigna del Sprint 3](sprint-3.md) vuelve a fijar el alcance con una lista de puntos
funcionales. El formato de las stories es el del [Sprint 2](sprint-2-backlog.md): narrativa,
**entre 2 y 4 criterios** y notas con dependencias, diseño y casos borde. Las reglas de los
criterios del [Sprint 1](sprint-1-backlog.md) siguen valiendo y no se repiten acá.

La numeración sigue donde quedó: el Sprint 2 llegó a US-33, así que este arranca en **US-34**.

## Dónde quedamos

El Sprint 2 cerró el mostrador de punta a punta: el pedido se paga (digital simulado o en la
caja), entra en la cola, la barra lo toma, lo marca listo y lo entrega escaneando el QR del
cliente, y el cliente lo ve todo en vivo.

Lo que este sprint encuentra hecho y no hay que rehacer:

- **El rol `Cashier`** existe, con su pantalla de caja (US-26). Ese punto de la consigna ya
  está cumplido.
- **El rol `Waiter`** existe y el administrador ya le puede crear la cuenta, pero no tiene
  ninguna pantalla.
- **Las cuentas de KDS** se dan de alta en el ABM de personal. **Una KDS es una barra**: la
  tablet es de la estación (§11). Se pueden crear varias, pero hoy **a todas les llegan los
  mismos pedidos**: la cola es la del local entero.
- **El stock** existe, **uno por producto** (`Product.Stock`), y confirmar ya rechaza un pedido
  sin unidades. No se parte por barra: lo que cambia es que pasa a ser **el stock de la noche**.
- **Los colores de espera del tablero** (ámbar a los 5 minutos, rojo a los 10) ya existen en
  el KDS. La alerta de este sprint es otra cosa: que se entere alguien que no está mirando la
  tablet.

Lo que **no existe** y este sprint crea: KDS simultáneas que reciben pedidos distintos, la
noche (evento), `Table`, `VipAccount`, el tipo de entrega en el pedido y cualquier pantalla de
métricas. Tampoco se guarda hoy **cuándo se tomó ni cuándo quedó listo** un pedido: el tablero
usa `LastModifiedAt` como reloj, y eso no sirve para medir tiempos después.

## Quiénes usan el sistema en este sprint

| Rol               | Qué hace                                                                  | Se autentica |
| ----------------- | ------------------------------------------------------------------------- | ------------ |
| **Cliente**       | Pide desde la barra o **desde su mesa VIP**, con saldo                    | No           |
| **KDS**           | La cuenta de una barra. **Ahora ve sólo los pedidos que le tocaron**      | Sí           |
| **Cajero**        | Cobra en efectivo. Si alcanza, coordina las mesas VIP                     | Sí           |
| **Mozo**          | **Estrena su pantalla**: toma las mesas listas y las entrega              | Sí           |
| **Administrador** | Arma la noche (KDS, personal y stock) y las mesas; **mira alertas y métricas** | Sí      |

## Arrastre del Sprint 2

| ID    | Story                                 | Estado                                                                |
| ----- | ------------------------------------- | --------------------------------------------------------------------- |
| US-24 | Pago digital con Mercado Pago         | Hecha en `feat/24-pago-digital-mercado-pago`. **No se mergea este sprint**: el pago digital sigue simulado |
| US-21 | Que me avise el celular               | Pendiente: no hay código de push ni claves VAPID                      |
| US-33 | Repartir los pedidos entre las barras | **La absorbe US-36**                                                  |

US-21 importa más que antes: el wireframe `MozoLogin` le pide permiso al mozo para avisarle
cuando una mesa queda lista, "el mismo aviso que le llega al cliente". Sin US-21, el mozo se
entera mirando la pantalla (US-43 lo cubre en tiempo real igual).

## Resumen

**La noche y las barras**: la base. Todo lo demás cuelga de que exista la noche. Una barra es
una KDS.

| ID    | Story                                              | Depende de   |
| ----- | -------------------------------------------------- | ------------ |
| US-35 | Armar una noche                                    | —            |
| US-37 | Cargar el stock de la noche                        | US-35        |
| US-36 | Distribuir los pedidos entre las KDS               | US-35        |
| US-38 | Que el pedido vaya a una KDS con capacidad         | US-36        |

> **US-34 se descartó** el 2026-10-06: proponía un ABM de barras aparte, pero una barra es una
> KDS, y las KDS ya se dan de alta en el ABM de personal.

**Sector VIP**: mesas, saldo y entrega en mesa.

| ID    | Story                                       | Depende de   |
| ----- | ------------------------------------------- | ------------ |
| US-39 | Dar de alta las mesas VIP                   | —            |
| US-40 | Abrir la cuenta de una mesa con su saldo    | US-35, US-39 |
| US-41 | Pedir desde la mesa VIP con el saldo        | US-36, US-40 |
| US-42 | Cubrir la diferencia si el saldo no alcanza | US-41        |

**El mozo**

| ID    | Story                             | Depende de |
| ----- | --------------------------------- | ---------- |
| US-43 | Ver las mesas listas y tomar una  | US-41      |
| US-44 | Marcar entregado en la mesa       | US-43      |
| US-45 | Coordinar las mesas desde la caja | US-43      |

**Alertas y métricas**

| ID    | Story                                   | Depende de |
| ----- | --------------------------------------- | ---------- |
| US-46 | Que me avise un pedido que espera mucho | US-36      |
| US-47 | Ver los tiempos de una noche            | US-35      |
| US-48 | Ver lo vendido en una noche             | US-35      |
| US-49 | Ver los productos más pedidos           | US-35      |

---

## La noche y las barras

### US-35 · Armar una noche

> **Como** administrador
> **quiero** armar la noche de hoy con su horario, las KDS que van a estar andando y quiénes
> trabajan
> **para** tener todo lo de esa noche en un solo lugar y que nadie de afuera de ella opere.

**Criterios de aceptación**

1. **Dado** que estoy en la administración, **cuando** creo una noche con nombre, inicio, fin y
   el personal que trabaja (al menos una KDS y un cajero), **entonces** queda en el listado, y
   se rechaza si se superpone con otra noche del local o si le falta la KDS o el cajero.
2. **Dado** que hay una noche en curso, **cuando** un cliente confirma un pedido, **entonces**
   el pedido queda asociado a esa noche.
3. **Dado** que no hay ninguna noche en curso, **cuando** un cliente abre la carta, **entonces**
   la ve pero no puede confirmar, y la pantalla le dice que el local no está tomando pedidos.
4. **Dado** una cuenta de personal (KDS, cajero o mozo) que no está en la noche en curso,
   **cuando** entra, **entonces** puede entrar, pero no ve ningún pedido de esa noche y la
   pantalla le dice que no está en la noche de hoy.

**Notas**

- **Depende de** nada. Va primera porque todo el sprint la necesita.
- **La noche es lo que organiza todo:** el stock (US-37), las KDS que reciben pedidos (US-36),
  quiénes trabajan (criterio 4), las cuentas VIP (US-40) y las métricas (US-47 a US-49), que
  pueden decir "el sábado" sin adivinar con fechas. Una noche de boliche cruza la medianoche,
  así que "pedidos del día" no sirve, igual que no sirvió para el turno de la caja (US-26).
- **En el código es `Night`** (decidido el 2026-10-07). `Event` se confunde con los eventos de
  dominio, y `Shift` ya nombra la ventana de cobros de la caja (`MyCollectionsHandler`).
- **El personal es KDS, cajero y mozo**, todas cuentas del ABM de personal. La noche guarda una
  sola lista de cuentas; "al menos una KDS y un cajero" se valida sobre los roles de esa lista.
  En la ficha pueden ser selectores separados por rol.
- **El mozo no es obligatorio acá.** Hace falta cuando la noche ofrece mesas VIP; esa regla
  entra con las stories del sector VIP (US-40), no con esta.
- **Criterio 4: entrar no se bloquea, se vacía.** El login funciona igual que hoy. Lo que filtra
  es la política de cada rol de estación (`Kds`, `Cashier`, y la del mozo cuando exista), que
  además del rol exige estar en la noche: los endpoints y los hubs responden 403 con su propio
  tipo, y la pantalla dice "no está en la noche de hoy". Una cuenta que se suma a mitad de
  camino no tiene que volver a entrar: el tablero de la KDS se completa solo al reintentar.
- **La noche que cuenta es la última que empezó**, no sólo la que está en curso (decidido el
  2026-10-07): los pedidos pagos se siguen preparando después del cierre, así que el personal
  del sábado conserva sus pantallas pasadas las 06:00, hasta que empieza la noche siguiente.
- **El administrador no se limita** por el criterio 4: ve todo para armar la noche siguiente o
  arreglar la de hoy.
- **Borde: la noche se extiende.** El administrador puede correr la hora de fin de una noche en
  curso. Acortarla a una hora que ya pasó es cerrarla.
- **Editar una noche** (decidido el 2026-10-07): desde el listado se entra a la ficha de cada
  noche. Una **próxima** se edita entera. Una **en curso** cambia nombre, fin y personal, pero
  **no el inicio**: sus pedidos ya son de ella. Una **terminada** sólo se mira, porque sus
  métricas se leen contra esos horarios.
- **El personal se elige con un desplegable por rol** (KDS, cajeros, mozos), con buscador
  cuando el rol tiene muchas cuentas.
- **Borde: termina la noche con pedidos pagos sin entregar.** Se siguen preparando y entregando:
  lo que se cierra es confirmar pedidos nuevos, no la barra. Un carrito armado a las 05:58 que se
  confirma a las 06:01 se rechaza con el mismo mensaje del criterio 3.
- **Borde: una cuenta que se saca de la noche en curso** deja de ver los pedidos en la próxima
  consulta, sin cerrarle la sesión: la pertenencia se mira contra la noche, no contra el token.
- **Diseño: no está dibujado.** Listado y ficha de la noche, con la misma piel que la
  administración. La ficha elige KDS y personal de las cuentas activas del ABM.

### US-37 · Cargar el stock de la noche

> **Como** administrador
> **quiero** cargar al armar la noche cuántas unidades hay de cada producto
> **para** saber con qué arrancamos y que no se venda lo que no hay.

**Criterios de aceptación**

1. **Dado** que armo una noche, **cuando** abro su stock, **entonces** cada producto arranca con
   lo que quedó de la noche anterior, y puedo sumar o restar unidades.
2. **Dado** una noche en curso, **cuando** un cliente confirma un pedido con más unidades de las
   que quedan, **entonces** el sistema no lo confirma y le dice qué producto falta.
3. **Dado** una noche terminada, **cuando** miro su stock, **entonces** veo cuánto se cargó,
   cuánto se vendió y cuánto quedó de cada producto.

**Notas**

- **Depende de** US-35.
- **No se parte por barra** (decidido el 2026-10-06): todas las KDS sirven del mismo stock. Lo
  que cambia es que el stock **es de la noche**, no del producto suelto.
- **El criterio 2 ya existía** para el stock único: confirmar descontaba `Product.Stock` y
  rechazaba si no alcanzaba (`OrderErrors.StockMoved` cubre la carrera por la última unidad).
  Cambia de dónde se descuenta, no la regla. Cancelar (US-23) devuelve a la noche del pedido.
- **El ajuste suma o resta, no fija un total**, como `AdjustStock` hoy: así no pisa una venta
  hecha mientras la pantalla estaba abierta.
- **Migración:** el `Product.Stock` de hoy pasa a ser el de la primera noche que se arme.
- **Diseño: no está dibujado.** Una lista de productos dentro de la ficha de la noche, con el
  ajuste en cada fila. El campo de stock de `AdminProductoEdit` se va.

**Construida el 2026-10-10.** Decidido ese día:

- **El stock es de la noche.** Una fila por producto y noche (`NightStock`): lo **cargado**, lo
  que **queda**, y lo **vendido** se deriva de las dos, así que no pueden contradecirse. Vender y
  cancelar mueven sólo lo que queda, con la misma sentencia atómica de antes.
- **Arranca con lo que sobró de la noche anterior** (la última anterior que tuvo ese producto), o
  con el número con que se creó el producto si ninguna lo tuvo. **No se arma mientras la noche
  anterior no terminó**: hasta entonces no se sabe qué va a dejar, y la pantalla lo dice. El fin
  de una noche es exclusivo, así que a la hora exacta ya cuenta como terminada.
- **Se abre sola**, la primera vez que alguien la necesita: el administrador al mirarla, el
  primer pedido de la noche o el primer celular que abre la carta. Nadie la arma a mano antes
  de vender.
- **`Product.Stock` pasó a ser `InitialStock`** (la columna conserva el nombre): sólo dice con
  cuántos arranca el producto la primera noche en que se vende, y ninguna venta lo mueve. El alta
  lo pide como "Stock inicial"; la edición ya no tiene stock.
- **La carta** decide "agotado" con lo que tiene la noche en curso. **Sin noche en curso no marca
  nada como agotado**: no hay un stock del cual quedarse sin nada, y la pantalla ya avisa que el
  local no toma pedidos.
- **Cancelar devuelve a la noche del pedido**, no a la que esté en curso. Un pedido anterior a las
  noches no tiene ninguna, y sus tragos vuelven al producto, de donde salieron.
- **Se retiró** el ajuste de stock del producto (`adjust-stock`), la regla de que un producto
  agotado no se puede volver a poner a la venta, y las columnas de stock y "sin stock" del
  listado de productos.
- **Migración de datos:** a cada noche que ya existía se le arma su stock a partir del
  `Product.Stock` de ese momento, que ya venía descontado de todas las ventas, de modo que la
  cadena de noches termina justo en ese número. Lo vendido sale de los pedidos de cada noche, sin
  contar los cancelados.
- **Hecho:** criterios 1, 2 y 3. El 3 (cargado, vendido y quedó de una noche terminada) se lee en
  la misma lista de la ficha, sin controles para moverlo.

### US-36 · Distribuir los pedidos entre las KDS

> **Como** local con más de una barra
> **quiero** que cada pedido pagado le llegue a una sola KDS, a la que tenga menos trabajo
> **para** que ninguna barra se sature mientras otra está libre, y que dos barras nunca
> preparen el mismo pedido.

**Criterios de aceptación**

1. **Dado** una noche con dos KDS, **cuando** se paga un pedido, **entonces** aparece en la cola
   de una sola de ellas, y la otra no lo ve.
2. **Dado** que una KDS tiene más pedidos esperando que otra, **cuando** se paga uno nuevo,
   **entonces** va a la que tiene menos.
3. **Dado** que el pedido cayó en una KDS, **cuando** el cliente mira su seguimiento,
   **entonces** sabe en qué barra lo retira.
4. **Dado** una noche con una sola KDS, **cuando** se paga un pedido, **entonces** va a ella,
   como hoy, sin configurar nada.

**Notas**

- **Depende de** US-35, que dice qué KDS andan esa noche. **Absorbe US-33** del Sprint 2.
- **"Menos trabajo"** cuenta los pedidos en `Queued` e `InPreparation` de cada KDS. Era la
  primera pregunta abierta de US-33. Contar tragos sería más justo, pero es más difícil de
  explicar y de probar; se cambia si en la demo no alcanza.
- **El pedido se asigna al pagarse**, no al confirmarse: un pedido en efectivo no ocupa a
  ninguna barra mientras espera la caja.
- **Multi-tenancy:** la KDS sale **del claim del token**, igual que el local. Nunca de la URL:
  una tablet no puede pedir la cola de otra cambiando la ruta. Necesita su test de aislamiento
  entre KDS, además del de locales.
- **Concurrencia:** con este reparto cada pedido llega a una sola tablet. El token de
  concurrencia de `Order` (`Version`, #26) ya cubre el caso de dos pantallas sobre el mismo
  pedido.
- **Diseño:** el seguimiento suma una línea, "Retiralo en Barra Pista", y el tablero muestra el
  nombre de su KDS en el header. Para que eso se lea, la cuenta de KDS necesita un nombre visible
  ("Barra Pista"), no sólo su usuario.
- **Las marcas de tiempo del pedido van acá** (`PreparationStartedAt`, `ReadyAt`), aunque
  ningún criterio las pida. Es la primera story que migra `Orders`, y las métricas (US-47) sólo
  pueden medir los pedidos hechos después: cuanto antes entren, más datos hay para la demo. Es
  la misma lección que US-30.

### US-38 · Que el pedido vaya a una KDS con capacidad

> **Como** cliente
> **quiero** que mi pedido vaya a una barra que lo pueda atender
> **para** no quedar esperando en una tablet apagada o tapada de pedidos.

**Criterios de aceptación**

1. **Dado** una KDS que se desconectó (la tablet se apagó o se quedó sin red), **cuando** se
   paga un pedido, **entonces** no se lo asigna a ella mientras siga desconectada.
2. **Dado** una KDS que tiene pedidos sin tomar y se cierra en medio de la noche, **cuando**
   se cierra, **entonces** esos pedidos pasan a otra KDS de la noche conservando su antigüedad.
3. **Dado** que todas las KDS de la noche están desconectadas, **cuando** se paga un pedido,
   **entonces** queda en espera y entra en la primera que vuelva, sin perderse.

**Notas**

- **Depende de** US-36.
- **Responde la tercera pregunta de US-33:** qué pasa con los pedidos de una barra que se
  cierra en medio de la noche (criterio 2).
- **"Desconectada"** sale de la conexión de SignalR del tablero: el hub ya sabe quién está
  conectado. Una caída corta de red no puede mandar los pedidos de un lado al otro, así que
  hace falta un margen (por ejemplo, un minuto) antes de considerarla afuera.
- **Sólo se mueven los pedidos sin tomar** (`Queued`). Uno que la barra ya está preparando no
  se reasigna: el trago está a medio hacer en esa barra.
- **El cliente se entera** si su pedido cambió de barra: la línea "Retiralo en…" de US-36 se
  actualiza sola, con el mismo mecanismo de US-22.
- **Un tope de pedidos por KDS** ("no más de 15 en cola") sería otra forma de capacidad. Queda
  afuera: con el reparto por carga de US-36, una barra sólo se llena si todas se llenan.

---

## Sector VIP

### US-39 · Dar de alta las mesas VIP

> **Como** administrador
> **quiero** cargar las mesas del sector VIP con su número
> **para** poder venderlas como paquete y que los pedidos lleguen a la mesa correcta.

**Criterios de aceptación**

1. **Dado** que estoy en la administración, **cuando** doy de alta una mesa con su número,
   **entonces** aparece en el listado, y un número repetido en el mismo local se rechaza.
2. **Dado** una mesa que ya no se usa, **cuando** la doy de baja, **entonces** deja de aparecer
   para abrirle cuenta, y sus pedidos viejos siguen diciendo "Mesa 5".

**Notas**

- **Depende de** nada. Puede ir en paralelo con las de barras.
- **El número es único por local**, no global (índice compuesto con `VenueId`, como dice
  `CLAUDE.md`): dos locales pueden tener su "Mesa 5".
- **Diseño: no está dibujado.** Es el ABM más chico del sistema.
- **Borde:** dar de baja una mesa con una cuenta abierta (US-40) se rechaza.

### US-40 · Abrir la cuenta de una mesa con su saldo

> **Como** administrador
> **quiero** abrir la cuenta de una mesa para la noche con el saldo del paquete que se vendió
> **para** darle a quien la compró un código que sólo sirve para esa mesa esa noche.

**Criterios de aceptación**

1. **Dado** una mesa sin cuenta abierta en la noche, **cuando** la abro con un saldo,
   **entonces** obtengo un código y su QR para entregarle en mano a quien compró la mesa.
2. **Dado** una cuenta abierta, **cuando** le cargo más saldo, **entonces** se suma a lo que
   queda, sin pisar lo que se gastó mientras tanto.
3. **Dado** que termina la noche, **cuando** alguien usa el código de esa cuenta, **entonces**
   ya no sirve para pedir.
4. **Dado** que el código se filtró, **cuando** lo regenero, **entonces** el anterior deja de
   servir y el saldo sigue igual.

**Notas**

- **Depende de** US-35 y US-39.
- **§8.2 del diseño funcional:** el código **no** se pega en la mesa. Lo entrega el mozo en
  mano o por WhatsApp a quien compró el paquete. El sistema no verifica personas: identifica la
  mesa, y el control de acceso al sector VIP lo hace el portero.
- **El código es un secreto como el `TrackingToken`**, no un número corto: quien lo tenga
  gasta el saldo. Va en el QR, que abre la carta de esa mesa.
- **El saldo se toca con token de concurrencia** (`VipAccount`, como dice `CLAUDE.md`). Es
  plata: dos celulares de la misma mesa pidiendo a la vez no pueden gastar dos veces lo mismo.
- **Borde: el saldo que sobra** al terminar la noche no se devuelve ni pasa a otra noche. Si
  el local quiere otra cosa, es una decisión de producto para otro sprint.
- **Diseño: no está dibujado.** La ficha de la mesa con "Abrir cuenta", el saldo y el QR para
  mostrar o compartir.

### US-41 · Pedir desde la mesa VIP con el saldo

> **Como** cliente de una mesa VIP
> **quiero** pedir desde mi celular y que se descuente del saldo de la mesa
> **para** no pagar trago por trago y que me lo traigan sin levantarme.

**Criterios de aceptación**

1. **Dado** que entré con el código de mi mesa, **cuando** abro la carta, **entonces** veo
   "Mesa 5" y el saldo disponible.
2. **Dado** que el saldo alcanza, **cuando** confirmo, **entonces** se descuenta, el pedido
   entra en la cola sin elegir método de pago, y la barra lo ve con "Mesa 5" en vez de "Retiro
   en barra".
3. **Dado** que dos celulares de la mesa confirman a la vez y el saldo alcanza sólo para uno,
   **cuando** se procesan, **entonces** uno se descuenta y el otro ve el saldo actualizado sin
   que se descuente nada.
4. **Dado** un código vencido, regenerado o inventado, **cuando** lo uso, **entonces** la app me
   lo dice y me ofrece pedir normal, para retirar en la barra.

**Notas**

- **Depende de** US-36 y US-40.
- **El tipo de entrega sale de cómo entró el cliente**: con el código de la mesa,
  `TableDelivery`; sin él, `BarPickup`. No se pregunta. Que un cliente VIP elija retirar en la
  barra queda afuera.
- **Es la tercera `IPaymentStrategy`**, la que el diseño ya preveía: saldo VIP. Agregarla no
  tiene que tocar el flujo de confirmar.
- **El pedido de mesa también se reparte entre las KDS (US-36)**, como cualquier otro. Que el
  sector VIP tenga una barra propia que reciba sólo sus pedidos queda afuera; si el local lo
  necesita, es una regla más del reparto.
- **En la barra, un pedido de mesa no se entrega con el QR del cliente**: queda en "Listos"
  hasta que lo toma el mozo (US-43). El botón "Entregado" del KDS no aparece en esas tarjetas.
- **Diseño:** `Pago` ya dibuja "Saldo de la mesa · Disponible $ [SALDO]" como método de pago, y
  `EstadoMesa` es el seguimiento de un pedido de mesa ("Te lo llevan a la Mesa 5. Quedate
  donde estás").
- **Multi-tenancy:** el código de una mesa de otro local da el mismo error que uno inventado,
  como pasa con el QR de retiro (US-20).

### US-42 · Cubrir la diferencia si el saldo no alcanza

> **Como** cliente de una mesa VIP
> **quiero** pagar desde el celular lo que el saldo no cubre
> **para** no quedarme sin pedir porque faltan unos pesos.

**Criterios de aceptación**

1. **Dado** que el total supera el saldo, **cuando** llego al pago, **entonces** veo cuánto
   cubre el saldo y cuánto me falta pagar.
2. **Dado** que pago la diferencia, **cuando** se confirma el pago, **entonces** el saldo queda
   en cero y el pedido entra en la cola.
3. **Dado** que el pago de la diferencia falla o lo abandono, **cuando** vuelvo, **entonces** el
   saldo de la mesa sigue intacto y el pedido no entra en la cola.

**Notas**

- **Depende de** US-41.
- **Cierra la pregunta del §8.1** (y la del §15) por la opción que ya preferíamos: cubrir la
  diferencia con otro medio de pago. Ni bloquear ni saldo negativo.
- **"Pago simulado", dice la consigna, y así queda:** la diferencia se paga con la estrategia
  digital simulada que ya está en `dev`. Mercado Pago (US-24) no se mergea este sprint; cuando
  entre, la diferencia la usa sin tocar este flujo.
- **El criterio 3 decide el orden:** el saldo se descuenta **cuando se confirma el pago de la
  diferencia**, no antes. Descontarlo primero obliga a devolverlo si el pago falla, y eso es una
  devolución, que el modelo de datos descarta.
- **`PaymentMethod` deja de alcanzar.** Un pedido pagado con saldo más digital tiene dos
  importes. Las ventas (US-48) necesitan saber cuánto entró por cada lado, así que el pedido
  guarda el desglose y no sólo un método.
- **Borde:** el saldo baja entre que el cliente ve la diferencia y que paga (otro celular de la
  mesa pidió en el medio). El pago confirmado ya no alcanza. Se rechaza antes de cobrar: el
  cliente ve la nueva diferencia y vuelve a pagar por ese importe.
- **Borde:** saldo en cero. No es un caso aparte: la diferencia es el total.

---

## El mozo

### US-43 · Ver las mesas listas y tomar una

> **Como** mozo
> **quiero** ver desde mi celular las mesas con pedidos listos y avisar cuál llevo yo
> **para** que dos mozos no vayan a la misma mesa y ninguna se quede esperando.

**Criterios de aceptación**

1. **Dado** que entro con mi cuenta de mozo, **cuando** abro la pantalla, **entonces** veo los
   pedidos de mesa listos de mi local, el que más espera primero, con mesa, tragos, notas y hace
   cuánto esperan.
2. **Dado** una mesa libre, **cuando** toco "La llevo yo", **entonces** pasa a "Las que
   tomaste", y los demás mozos la ven en "Las lleva otro" con mi nombre, sin recargar.
3. **Dado** que dos mozos tocan "La llevo yo" en la misma mesa a la vez, **cuando** se procesa,
   **entonces** la toma uno solo y el otro ve quién la tomó.
4. **Dado** que entro con una cuenta que no es de mozo, **cuando** abro esa dirección,
   **entonces** el sistema no me la muestra.

**Notas**

- **Depende de** US-41. **Es la story que cumple el punto "Mozo" de la consigna**: el rol ya
  existe, lo que falta es entrar y tener pantalla.
- **Diseño: ya está dibujado.** `MozoLogin` y `MozoEntregas`, en celular. El login es el mismo
  que usan la caja y la barra; el wireframe dice "PIN", pero es la contraseña de siempre.
- **Nadie asigna mesas** (§8.3): cada mozo toma la que va a llevar. Es lo que dice el
  wireframe ("Nadie te asigna mesas: las tomás vos").
- **Cuando lo toma, el cliente se entera:** `EstadoMesa` muestra "Martín lo está llevando". Es
  la misma actualización en vivo de US-22.
- **En tiempo real** con el mismo mecanismo que la caja y el tablero: un hub o un grupo por
  local con la política del rol `Waiter`.
- **Soltar una mesa tomada por error** queda afuera de los criterios. Si hace falta en la
  prueba, se suma un "La suelto" como el "Devolver a la cola" del KDS.
- **El aviso al celular del mozo** (de `MozoLogin`) depende de US-21. Si US-21 no entra, el
  mozo se entera mirando la pantalla.

### US-44 · Marcar entregado en la mesa

> **Como** mozo
> **quiero** marcar que dejé el pedido en la mesa con un toque
> **para** cerrarlo y que salga de la lista de todos.

**Criterios de aceptación**

1. **Dado** una mesa que tomé, **cuando** toco "Entregado" y lo confirmo, **entonces** el pedido
   queda entregado, sale de las listas de todos los mozos y el cliente ve "En tu mesa".
2. **Dado** un pedido de mesa que tomó otro mozo, **cuando** lo miro, **entonces** no puedo
   marcarlo entregado.

**Notas**

- **Depende de** US-43.
- **Sin escaneo**, a propósito (§8.3): es un movimiento entre empleados, y el cliente de la mesa
  no tiene QR de retiro (`EstadoMesa`: "En mesa no hace falta QR: te lo entrega el mozo en
  mano").
- **"Entregado" pide confirmación** (lo dice el wireframe) en vez de ofrecer "Deshacer" como el
  KDS: el mozo tiene el celular en una mano y la bandeja en la otra.
- **Es `Order.Deliver`**, la misma transición de la barra. El pedido guarda además qué mozo lo
  entregó, para la auditoría.

### US-45 · Coordinar las mesas desde la caja

> **Como** cajero
> **quiero** ver qué mesas están listas y nadie tomó todavía
> **para** avisarle por radio a un mozo antes de que el trago se caliente.

**Criterios de aceptación**

1. **Dado** que estoy en la caja, **cuando** abro "Entregas VIP", **entonces** veo las mesas
   listas que no tomó nadie, la que más espera primero, y aparte las que ya tomó un mozo, con
   quién y hace cuánto.
2. **Dado** que un mozo toma o entrega una mesa, **cuando** tengo la pantalla abierta,
   **entonces** la veo cambiar sin recargar.

**Notas**

- **Depende de** US-43.
- **No la pide la consigna**: sale del §12 del diseño funcional. Es **la primera candidata a
  recortar** si el sprint se estira.
- **Diseño: ya está dibujado.** `CajeroVip`, como segunda solapa de la caja. Cuando se rediseñó
  la caja (US-26) se dejó "sin solapas" justamente porque esto no era de ese sprint: ahora
  `StaffHeader` ya acepta solapas.
- **Sólo mira**: el cajero no asigna ni marca nada. El sistema expone la información y la
  coordinación es de palabra (§8.3).

---

## Alertas y métricas

### US-46 · Que me avise un pedido que espera mucho

> **Como** encargado de la noche
> **quiero** ver en un solo lugar los pedidos que están esperando demasiado, de cualquier barra
> **para** reaccionar antes de que el cliente venga a reclamar.

**Criterios de aceptación**

1. **Dado** un pedido que lleva **más de 10 minutos** en la cola o en preparación, **cuando**
   tengo abierta la pantalla de alertas, **entonces** aparece con su número, su KDS y hace
   cuánto espera, sin recargar.
2. **Dado** un pedido listo que **nadie retiró en 15 minutos**, o una mesa lista que **ningún
   mozo tomó en 5**, **cuando** miro las alertas, **entonces** también aparece, diciendo cuál de
   las dos cosas pasa.
3. **Dado** un pedido con alerta, **cuando** avanza de etapa, **entonces** la alerta desaparece
   sola.
4. **Dado** que no hay nada demorado, **cuando** miro la pantalla, **entonces** me dice que está
   todo en tiempo, en vez de quedar en blanco.

**Notas**

- **Depende de** US-36 (la KDS de cada pedido).
- **Los 10 minutos son el rojo del tablero** (US-15), a propósito: la alerta dice lo mismo que la
  tablet, pero a alguien que no está parado delante de ella. Los 15 y los 5 son propuestas; se
  ajustan antes de tomarla.
- **Responde una pregunta del §15:** qué pasa con un pedido listo que nadie retira. Por ahora,
  avisa. Re-prepararlo o descartarlo sigue sin definir.
- **Para quién:** el administrador. Proponemos que sea la primera pantalla de la administración
  durante una noche en curso. Mostrarla también en la caja es fácil después.
- **El reloj de cada etapa:** la cola cuenta desde `PaidAt`; la preparación, desde
  `PreparationStartedAt`; y listo, desde `ReadyAt`. Las dos últimas columnas las crea US-36.
- **Diseño: no está dibujado.**

### US-47 · Ver los tiempos de una noche

> **Como** administrador
> **quiero** ver cuánto tardaron los pedidos en cada etapa durante una noche
> **para** saber si me falta gente en una barra o si el problema es la entrega.

**Criterios de aceptación**

1. **Dado** que elijo una noche, **cuando** abro sus métricas, **entonces** veo el tiempo
   promedio y el del peor 10 % desde el pago hasta que quedó listo, y desde que quedó listo
   hasta que se entregó.
2. **Dado** una noche con varias KDS, **cuando** miro los tiempos, **entonces** los veo
   también separados por KDS.
3. **Dado** pedidos hechos antes de que se guardaran las marcas de tiempo, **cuando** miro los
   tiempos, **entonces** quedan afuera y la pantalla dice cuántos no se pudieron medir.

**Notas**

- **Depende de** US-35 y de las columnas que agrega US-36.
- **Por qué el peor 10 % y no sólo el promedio:** con 200 pedidos rápidos y 20 de media hora,
  el promedio dice "8 minutos" y esconde a los veinte que se quejaron.
- **El criterio 3 es el criterio 4 de US-30** aplicado a las métricas: no se inventan tiempos
  para los pedidos viejos.
- **Los cancelados no cuentan**, y un pedido de mesa mide la entrega hasta que el mozo marcó
  "Entregado".
- **Es una query, no un agregado:** una interfaz de queries de métricas en `Application` con `AsNoTracking()` y proyección a
  DTO, como dice `CLAUDE.md`. No hace falta repositorio.
- **Diseño: no está dibujado.** Las métricas de US-47 a US-49 son una sola pantalla con tres
  bloques; se dibuja una vez.

### US-48 · Ver lo vendido en una noche

> **Como** administrador
> **quiero** ver cuánto se vendió en una noche y por qué medio entró
> **para** cerrar la noche sabiendo cuánto tiene que haber en la caja.

**Criterios de aceptación**

1. **Dado** que elijo una noche, **cuando** abro sus métricas, **entonces** veo el total vendido
   y la cantidad de pedidos, separados en efectivo, digital y saldo VIP.
2. **Dado** un pedido de mesa que pagó una parte con saldo y otra digital, **cuando** miro las
   ventas, **entonces** cada parte suma en su medio.
3. **Dado** pedidos cancelados o que nunca se pagaron, **cuando** miro las ventas, **entonces**
   no suman.

**Notas**

- **Depende de** US-35, y de US-42 para el criterio 2.
- **"Ventas simuladas"** porque el pago digital es simulado. La pantalla no tiene que decirlo:
  los números son los que guarda el sistema.
- **Vendido es pagado**: cuenta `PaidAt`, no la confirmación. Un pedido en efectivo que nadie
  cobró no es una venta.
- **Borde:** el saldo VIP. El paquete se cobró afuera del sistema cuando se vendió la mesa; lo
  que acá suma como "saldo VIP" es lo consumido, no lo que se cargó. Se dice en la pantalla para
  que nadie lo sume dos veces.

### US-49 · Ver los productos más pedidos

> **Como** administrador
> **quiero** ver qué productos se pidieron más en una noche
> **para** saber cuánto cargar de cada uno la próxima.

**Criterios de aceptación**

1. **Dado** que elijo una noche, **cuando** abro sus métricas, **entonces** veo los diez
   productos más pedidos, por unidades, con cuánto facturó cada uno.
2. **Dado** un producto del ranking, **cuando** lo miro, **entonces** veo también cuánto se había
   cargado de él esa noche y si se agotó.

**Notas**

- **Depende de** US-35.
- **Por unidades, no por pedidos:** un pedido de 4 Gin Tonic cuenta 4.
- **El nombre y el precio salen del ítem del pedido** (`unit_price` congelado), no del producto
  actual: un producto renombrado o dado de baja aparece igual, con lo que facturó esa noche.
- **El criterio 2 es el que contesta la pregunta de la narrativa**: cruza lo pedido con el stock
  de la noche (US-37). Un producto que se agotó a la 01:00 vendió menos de lo que podía, y el
  ranking solo no lo muestra.
- **Depende también de** US-37 para el criterio 2.

---

## Qué proponemos comprometer

**Primero, sí o sí: la noche y las barras (US-35 a US-38).** Son la base de todo lo demás: sin
noche no hay stock, ni personal, ni métricas, y sin el reparto el pedido VIP no sabe a qué KDS
ir. US-35 y US-39 no dependen de nada y pueden arrancar en paralelo.

**Después: el VIP y el mozo (US-39 a US-44).** Es el recorrido nuevo que se ve en la demo, y
casi todo está dibujado.

**Al final: alertas y métricas (US-46 a US-49).** Son queries sobre datos que ya existen para
entonces. Lo único que no puede esperar al final son las columnas de tiempo, y por eso entran
con US-36.

**Para recortar, en este orden:** US-45 (la caja coordinando, que la consigna no pide), el
criterio 3 de US-38 (todas las KDS caídas a la vez), el criterio 2 de US-49 y el criterio 2 de
US-47 (los tiempos por KDS). Ninguno deja un punto de la consigna sin cumplir.

## Antes de empezar

1. ~~**El nombre de "noche" en el código.**~~ Decidido el 2026-10-07: **`Night`**. Ya está en
   el glosario de `CLAUDE.md`, junto con **alerta** (`Alert`) y **métricas** (`Metrics`).
2. **Los umbrales de la alerta** (US-46): 10 minutos en la cola o en preparación, 15 listo sin
   retirar y 5 una mesa sin mozo. Proponemos fijos, no configurables.
3. **Diseño.** Están dibujados el mozo, la caja VIP, el seguimiento de mesa y el pago con saldo.
   **Falta dibujar**: la noche (con su stock), las mesas, la apertura de cuenta VIP, las alertas
   y las métricas. Son pantallas de administración y pueden usar la piel que ya tiene, pero las
   métricas sí necesitan un dibujo antes de construirlas.

## Deuda que arrastramos

La del Sprint 2 sigue igual: los tests de punta a punta no corren en la CI, la impresora
térmica y lo que quedó afuera del wireframe del KDS. **La de la CI duele más este
sprint**: el recorrido VIP tiene cuatro actores (cliente, barra, mozo y caja), y un spec así
que sólo corre en una máquina es uno que se rompe sin que nadie se entere.

**El push (US-21) pasa a tener dos clientes**: el cliente de barra y el mozo. Si no entra en este
sprint, conviene que sea lo primero del próximo.
