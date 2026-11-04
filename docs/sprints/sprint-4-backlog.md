# Backlog del Sprint 4

La [consigna del Sprint 4](sprint-4.md) fija el alcance con tres puntos funcionales. El formato de
las stories es el del [Sprint 2](sprint-2-backlog.md): narrativa, **entre 2 y 4 criterios** y notas
con dependencias, diseño y casos borde. Las reglas de los criterios del
[Sprint 1](sprint-1-backlog.md) siguen valiendo y no se repiten acá.

La numeración sigue donde quedó: el Sprint 3 llegó a US-49, así que este arranca en **US-50**.

## Dónde quedamos

Lo que este sprint encuentra hecho y no hay que rehacer:

- **El stock es por barra y por noche** (`BarStock`, US-37), y `Product.Stock` es el stock general
  que incluye lo cargado en las barras. Se descuenta con una sentencia condicional por producto,
  en la misma transacción que el pedido (`OrderRepository.TakeTheDrinksAsync`).
- **Un pedido va entero a una sola barra** (`BarChooser`, US-36), que tiene que tener todo. El
  efectivo elige barra y descuenta recién al cobrar (`CollectCashHandler`).
- **El precio ya se congela al confirmar** en `OrderItem.UnitPrice`, y el nombre en `ProductName`.
- **El total del pedido se deriva de sus ítems**, y de ahí salen el saldo VIP y las métricas.

Lo que **no existe** y este sprint crea: combos, promociones y cualquier forma de reconocer a un
cliente entre un pedido y el siguiente. Hoy no hay entidad `Customer` en el dominio.

> **El backlog del Sprint 3 quedó desactualizado en un punto:** dice que el stock "no se parte por
> barra", pero US-37 se construyó con un stock por barra. Este sprint parte de lo que hace el
> código, que además es lo que pide esta consigna ("la barra correspondiente").

## Quiénes usan el sistema en este sprint

| Rol               | Qué hace                                                       | Se autentica |
| ----------------- | -------------------------------------------------------------- | ------------ |
| **Cliente**       | **Pide combos, ve las promos y junta sellos desde su celular** | No           |
| **KDS**           | **Ve de qué está hecho cada combo** que tiene que preparar     | Sí           |
| **Cajero**        | Cobra en efectivo, con el precio congelado al confirmar        | Sí           |
| **Administrador** | **Arma los combos, las promociones y la tarjeta de sellos**    | Sí           |

## Resumen

**Combos**

| ID    | Story                                  | Depende de |
| ----- | -------------------------------------- | ---------- |
| US-50 | Armar un combo                         | —          |
| US-51 | Descontar los componentes en la barra  | US-50      |
| US-52 | Pedir un combo desde la carta          | US-51      |
| US-53 | Ver el combo en el tablero de la barra | US-51      |

**Promociones por franja horaria**

| ID    | Story                             | Depende de |
| ----- | --------------------------------- | ---------- |
| US-54 | Configurar una promoción          | —          |
| US-55 | Aplicar la promoción al confirmar | US-54      |
| US-56 | Ver la promoción en la carta      | US-55      |

**Gamification: tarjeta de sellos**

| ID    | Story                           | Depende de   |
| ----- | ------------------------------- | ------------ |
| US-57 | Configurar la tarjeta de sellos | —            |
| US-58 | Juntar sellos                   | US-57        |
| US-59 | Canjear el regalo               | US-51, US-58 |

---

## Combos

### US-50 · Armar un combo

> **Como** administrador
> **quiero** armar un combo con varios productos y un precio propio
> **para** vender varias cosas juntas sin cargar un stock aparte.

**Criterios de aceptación**

1. **Dado** que estoy en la administración, **cuando** creo un combo con nombre, categoría, precio
   y sus componentes con la cantidad de cada uno, **entonces** queda en el listado de productos
   marcado como combo.
2. **Dado** un combo con menos de dos unidades en total, o con otro combo entre sus componentes,
   **cuando** lo guardo, **entonces** se rechaza y la pantalla dice por qué.
3. **Dado** un combo, **cuando** cambio sus componentes, **entonces** sólo cambian los pedidos
   nuevos: los ya confirmados conservan los componentes con los que se confirmaron.

**Notas**

- **Un combo es un `Product` con componentes**, no una entidad aparte. Así reutiliza la carta, el
  carrito, la foto, la categoría, el interruptor de disponibilidad y `OrderItem.ProductId`. Una
  entidad aparte obligaría a duplicar todo eso y a volver nulo el producto del ítem.
- **El combo no tiene stock propio.** Quedan afuera de él `AdjustStock`, la carga de stock de la
  noche y su reporte. Hoy `IsSoldOut` es `Stock == 0` y `MarkAvailable` rechaza un agotado: esas
  dos reglas no pueden aplicarse a un combo tal como están.
- **El precio lo pone el administrador.** No se valida contra la suma de los componentes.
- **Criterio 2:** "dos unidades" admite un balde de cuatro cervezas iguales.

### US-51 · Descontar los componentes en la barra

> **Como** administrador
> **quiero** que confirmar un combo descuente cada componente de la barra que lo prepara
> **para** que el stock de la noche diga lo que queda de verdad.

**Criterios de aceptación**

1. **Dado** un combo de 2 Fernet y 1 hielo, **cuando** se paga un pedido con 2 de ese combo,
   **entonces** la barra asignada baja 4 Fernet y 2 hielos, y el stock general baja lo mismo.
2. **Dado** que ninguna barra tiene todos los componentes del pedido, sumados a los productos
   sueltos, **cuando** se confirma o se cobra, **entonces** se rechaza entero, y el mensaje nombra
   lo que el cliente pidió (el combo, no el componente), sin descontar nada.
3. **Dado** un combo en efectivo, **cuando** se confirma y el administrador cambia sus componentes
   antes del cobro, **entonces** al cobrar se descuentan los componentes congelados al confirmar.
4. **Dado** un combo vendido, **cuando** abro el stock de la noche, **entonces** el vendido de cada
   componente incluye lo que salió en combos.

**Notas**

- **Es el corazón del punto de la consigna y va antes que la pantalla.** Se cubre con test de
  integración contra la base, igual que la carrera por la última unidad.
- **Un solo lugar calcula las unidades.** `Order` expone lo que necesita por producto: combos
  desarmados y sumados con los sueltos. De ahí leen `BarChooser` (que no cambia, recibe esas
  líneas), `TakeTheDrinksAsync` y `SaveCancellationAsync`. Sumar importa: un combo con Fernet más
  un Fernet suelto no puede pasar el chequeo leyendo cada línea por separado.
- **Los componentes se congelan en el ítem**, en una tabla hija de `OrderItem`, por el criterio 3.
  Es la misma razón por la que se congela `UnitPrice`.
- **Hay que corregir al hacer esta story:**
    - `CollectCashHandler.SoldOut` busca el nombre con `Items.First(ProductId == missing)`. Con un
      componente faltante, tiraría una excepción. `ConfirmOrderHandler` hace lo mismo sobre el menú.
    - `NightStockQueries` calcula el vendido desde `order.Items`: un combo no lo movería (criterio 4).
    - `ProductQueries` decide si se ofrece mirando `BarStock` del propio producto (ver US-52).
- **El ranking de US-49 cuenta el combo como combo.** Es lo que se vendió. El stock de la noche sí
  lo desarma, porque lo que hay que cargar la próxima son los componentes.

### US-52 · Pedir un combo desde la carta

> **Como** cliente
> **quiero** ver los combos en la carta y saber qué traen
> **para** pedir varias cosas juntas con un toque.

**Criterios de aceptación**

1. **Dado** un combo activo y disponible, **cuando** abro la carta, **entonces** lo veo en su
   categoría, con su precio y lo que trae.
2. **Dado** un combo que ninguna barra de la noche puede armar entera, o con un componente dado de
   baja o apagado, **cuando** abro la carta, **entonces** aparece agotado y no se puede agregar.
3. **Dado** un combo en mi pedido, **cuando** lo confirmo, **entonces** entra al flujo igual que
   cualquier producto, con cualquier método de pago.

**Notas**

- **Criterio 2:** "armar entera" significa que una misma barra tiene todos los componentes. Es la
  misma regla de `BarChooser`, repetida en SQL en la carta.
- **Spec de Playwright:** es pantalla del flujo principal.

### US-53 · Ver el combo en el tablero de la barra

> **Como** bartender
> **quiero** ver qué trae cada combo
> **para** prepararlo sin tener que acordarme de la composición.

**Criterios de aceptación**

1. **Dado** un pedido con un combo, **cuando** lo veo en el tablero, **entonces** veo el combo con
   sus componentes y cantidades debajo.
2. **Dado** un combo modificado después de confirmado el pedido, **cuando** lo veo en el tablero,
   **entonces** veo los componentes congelados en ese pedido.

**Notas**

- **Depende de** US-51, que congela los componentes.
- **La nota del ítem sigue siendo una**: "sin hielo" vale para el combo entero, no por componente.

---

## Promociones por franja horaria

### US-54 · Configurar una promoción

> **Como** administrador
> **quiero** definir un descuento para ciertos productos, ciertos días y en cierta franja horaria
> **para** armar un happy hour sin tener que cambiar los precios a mano.

**Criterios de aceptación**

1. **Dado** que estoy en la administración, **cuando** creo una promoción con nombre, porcentaje,
   días, hora de inicio, hora de fin y productos, **entonces** queda en el listado, activa.
2. **Dado** una promoción de viernes de 23:00 a 01:00, **cuando** son las 00:30 del sábado,
   **entonces** está vigente; a las 00:30 del viernes no lo está.
3. **Dado** un porcentaje fuera de 1 a 99, sin días, sin productos, o con inicio igual a fin,
   **cuando** la guardo, **entonces** se rechaza y la pantalla dice por qué.
4. **Dado** una promoción, **cuando** la desactivo, **entonces** deja de aplicarse a los pedidos
   nuevos, y los confirmados conservan su precio.

**Notas**

- **La franja que cruza la medianoche pertenece al día en que empieza** (criterio 2). Una noche de
  boliche cruza la medianoche, así que es el caso normal. La regla vive en el dominio.
- **Las horas son locales del boliche**, y el servidor corre en UTC. Se resuelven con
  `America/Argentina/Buenos_Aires`, en una constante: hoy hay un solo país. Con un local en otro
  huso, pasa a ser columna de `Venue`.
- **Sólo porcentaje sobre productos elegidos**, combos incluidos. El 2x1 y la promo por categoría
  quedan afuera (ver "Lo que no entra").

### US-55 · Aplicar la promoción al confirmar

> **Como** cliente
> **quiero** pagar el precio de la promoción vigente sin hacer nada
> **para** aprovechar el happy hour.

**Criterios de aceptación**

1. **Dado** un producto con una promoción vigente de 30%, **cuando** confirmo el pedido,
   **entonces** ese ítem queda a 70% de su precio, con redondeo a dos decimales, y el total y el
   pago lo reflejan con cualquier método.
2. **Dado** un producto con dos promociones vigentes, **cuando** confirmo, **entonces** se aplica
   sólo la de mayor descuento, sin acumularlas.
3. **Dado** un pedido en efectivo confirmado dentro de la franja, **cuando** se cobra después de
   que terminó, **entonces** se cobra el precio con promoción.
4. **Dado** que el precio cambió desde que armé el pedido, **cuando** confirmo, **entonces** se
   rechaza sin cobrar nada, y la pantalla me muestra el total nuevo para que decida.

**Notas**

- **Se decide con el reloj del servidor**, nunca con lo que mande el celular, y el precio se
  congela en `UnitPrice`, como hasta ahora. Las métricas de ventas (US-48) quedan bien sin tocarlas.
- **El ítem guarda además el precio de lista y el nombre de la promoción**, para que el seguimiento
  y el ticket digan qué se descontó. Son nulos en los pedidos anteriores.
- **Criterio 4:** el carrito abierto a la 01:00 en punto es un caso normal de un happy hour, y
  cobrar más de lo que la pantalla mostraba no es aceptable. El pedido manda el total que vio y se
  compara. `VipBalancePaymentStrategy` ya hace algo parecido (`BalanceChanged`).
- **Un reintento idempotente devuelve el pedido ya escrito**, con su precio original.

### US-56 · Ver la promoción en la carta

> **Como** cliente
> **quiero** ver en la carta qué está en promoción y cuánto cuesta ahora
> **para** decidir qué pedir.

**Criterios de aceptación**

1. **Dado** un producto con una promoción vigente, **cuando** abro la carta, **entonces** veo el
   precio con promoción, el precio de lista tachado y el nombre de la promoción.
2. **Dado** un pedido con promociones, **cuando** lo sigo, **entonces** veo cuánto me ahorré.

**Notas**

- **Depende de** US-55. El cálculo es uno solo: la carta y la confirmación llaman a la misma regla.
- **Cachés:** la carta puede quedar vieja en el celular. Eso no es un problema de cobro, porque lo
  cubre el criterio 4 de US-55.

---

## Gamification: tarjeta de sellos

### US-57 · Configurar la tarjeta de sellos

> **Como** administrador
> **quiero** definir cuántos sellos hacen falta y qué producto se regala
> **para** premiar a quien vuelve a pedir, a un costo que yo controlo.

**Criterios de aceptación**

1. **Dado** que estoy en la administración, **cuando** activo la tarjeta con una cantidad de sellos
   de 2 o más y un producto de regalo que no sea combo, **entonces** los clientes empiezan a sumar.
2. **Dado** que cambio la cantidad o el regalo, **cuando** un cliente ya tiene sellos, **entonces**
   conserva sus sellos, y la regla nueva se aplica a su progreso.
3. **Dado** que desactivo la tarjeta, **cuando** un cliente abre la carta, **entonces** no la ve, no
   suma ni canjea, y conserva sus sellos por si se vuelve a activar.

**Notas**

- **Una configuración por boliche**, con `VenueId` como todo lo demás. Sin este alta, no hay tarjeta.

### US-58 · Juntar sellos

> **Como** cliente
> **quiero** sumar un sello por cada trago que retiro, sin crear una cuenta
> **para** ganarme uno de regalo.

**Criterios de aceptación**

1. **Dado** un primer pedido desde mi celular, **cuando** lo confirmo, **entonces** el servidor me da
   una tarjeta, queda guardada en el celular y las próximas veces viene sola.
2. **Dado** un pedido de 3 unidades con mi tarjeta, **cuando** se entrega, en barra o en mesa,
   **entonces** sumo 3 sellos, y los veo en la carta y en el seguimiento.
3. **Dado** un pedido cancelado, sin pagar, o con la entrega deshecha (US-18), **cuando** veo mi
   tarjeta, **entonces** no suma sellos.
4. **Dado** el token de la tarjeta de otro boliche, **cuando** pido, **entonces** no suma en ningún
   lado: la tarjeta es por boliche.

**Notas**

- **El token lo genera el servidor**, es largo y no se adivina, como `TrackingToken`. Se guarda en
  `localStorage`. Borrar los datos o cambiar de celular pierde los sellos; eso se decidió el
  2026-11-04, al elegir no tener cuenta.
- **Los sellos se derivan, no se guardan:** son las unidades de los pedidos entregados de esa
  tarjeta. Así deshacer una entrega no necesita compensar nada, y el contador no puede contradecir
  a los pedidos. `Order` guarda de qué tarjeta es.
- **Un combo suma un sello por unidad de combo**, no por componente. El regalo no suma.
- **El sello se gana al entregar, no al confirmar.** Es lo que limita el abuso: un sello sólo sale
  de un trago pagado y retirado. Lo que queda abierto es compartir la tarjeta entre amigos, y
  conseguir el regalo juntos es un costo aceptable; igual pagaron cada trago.
- **Spec de Playwright:** es pantalla del flujo principal.

### US-59 · Canjear el regalo

> **Como** cliente
> **quiero** agregar a mi pedido el trago que me gané
> **para** no pagarlo.

**Criterios de aceptación**

1. **Dado** una tarjeta con los sellos completos, **cuando** armo un pedido con al menos un producto
   pago, **entonces** puedo agregar el regalo, que va a $0 y descuenta los sellos de una vez.
2. **Dado** un regalo agotado o apagado, **cuando** intento canjearlo, **entonces** se rechaza, sin
   gastar sellos ni confirmar el pedido.
3. **Dado** un pedido en efectivo con regalo, **cuando** se cancela, **entonces** los sellos vuelven.
4. **Dado** dos celulares con la misma tarjeta y sellos para un solo regalo, **cuando** canjean a la
   vez, **entonces** sólo uno lo logra, y el otro ve su tarjeta actualizada.

**Notas**

- **Depende de** US-51: el regalo y el mismo producto pago pueden ir juntos en un pedido, y las
  unidades tienen que sumarse para descontar el stock.
- **Tiene que acompañar a algo pago** (criterio 1). Un pedido de total cero abriría casos en la
  caja, en el pago digital y en el saldo VIP, sin que nadie lo pida.
- **Hay que cambiar el dominio:** `Order` rechaza hoy precios de cero y dos líneas del mismo
  producto. El regalo se modela explícito en el ítem, y el canje lo valida el servidor. El celular
  nunca manda un precio.
- **El criterio 4 es dinero.** Se resuelve con el token de concurrencia que ya usa `VipAccount`.
- **Un efectivo sin cobrar de una noche pasada no debería retener sellos.** Depende de la pregunta
  abierta del §15 sobre el pedido que nunca se paga; hasta entonces, esos no cuentan como canjeados.
- **El regalo aparece a $0 en ventas y cuenta como unidad en el ranking de US-49.** Salió de la barra.

---

## Lo que no entra

| Qué                                    | Por qué                                                                            |
| -------------------------------------- | ---------------------------------------------------------------------------------- |
| Cuenta de cliente                      | Descartada el 2026-11-04: el token alcanza y pedir sin cuenta es parte del diseño. |
| Insignias y ranking de clientes        | La mecánica elegida es la tarjeta de sellos.                                       |
| Gamification para el personal          | La consigna no lo pide, y se eligió apuntar al cliente.                            |
| 2x1 y promo por categoría              | "Por ejemplo, happy hour" se cumple con porcentaje sobre productos.                |
| Promociones atadas a una noche         | Descartadas el 2026-11-04: se repiten por semana.                                  |
| Repartir un pedido entre varias barras | Un pedido va a una barra (US-36). Partirlo cambiaría ticket, QR y cola.            |
| Combos dentro de combos                | Nadie lo pidió, y complica el desarmado de stock.                                  |

## Qué proponemos comprometer

**Primero: US-50 y US-51.** Es el punto más concreto de la consigna, y el que cambia cómo se calcula
el stock del pedido. US-59 se apoya en ese cambio.

**En paralelo: US-54 y US-57.** No tocan el pedido y no dependen de nada, así que no chocan con los
combos.

**Después, de a una: US-55 y US-58.** Tocan `ConfirmOrderHandler` y `Order`, igual que US-51, así que
conviene que no corran a la vez.

**Al final: las pantallas de cliente y el canje** (US-52, US-53, US-56 y US-59).

**Para recortar, en este orden:** el criterio 2 de US-56, el criterio 2 de US-57 y el criterio 3 de
US-57. Ninguno deja un punto de la consigna sin cumplir.

## Antes de empezar

1. **El glosario.** Faltan términos, y `CLAUDE.md` pide acordarlos antes de inventarlos. Proponemos:

    | Docs (español)    | Código (inglés)  |
    | ----------------- | ---------------- |
    | Combo             | `Combo`          |
    | Componente        | `ComboComponent` |
    | Promoción         | `Promotion`      |
    | Tarjeta de sellos | `StampCard`      |
    | Sello             | `Stamp`          |
    | Regalo / canje    | `Reward`         |

2. **Diseño.** Nada de este sprint está dibujado. La carta con precio tachado, la tarjeta de sellos y
   el canje son pantallas del cliente, y necesitan un dibujo antes de construirlas. Las de
   administración pueden usar la piel que ya tienen.

## Deuda que arrastramos

Los tests de punta a punta siguen sin correr en la CI, y este sprint suma pantallas del flujo
principal. El push (US-21) y la impresora térmica siguen pendientes; la consigna no los pide.
