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
| US-14 | Agrupar la carta por categoría | US-06, US-09 | ✅ Terminada |

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

**Deuda del tablero** — salió de auditar US-15 ya mergeada. Ninguna rompe un criterio, pero
las dos dejan a la barra sin ver algo sin que nadie se entere.

| ID    | Story                                         | Depende de |
| ----- | --------------------------------------------- | ---------- |
| US-31 | Que el tablero se ponga al día solo           | US-15      |
| US-32 | Que el tablero me mande a entrar si se venció | US-15      |
| US-33 | Repartir los pedidos entre las barras         | US-16      |

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

**Decidido el 2026-09-27 y construido:**

- **Columnas** `CreatedAt`, `CreatedBy`, `LastModifiedAt` y `LastModifiedBy` en `Products`,
  `StaffUsers` y `Categories`; sólo `CreatedAt` en `Venues` y `Orders`; nada en `OrderItems` ni
  en `OrderCodeCounters`. El porqué de cada una está en
  [modelo-de-datos.md](../modelo-de-datos.md).
- **Un solo lugar:** un interceptor de `SaveChanges` lee el reloj y el username del token. La
  venta que descuenta stock va por su propia sentencia y no cuenta como una edición.
- **Dónde se ve:** al pie de la ficha de personal y de la ficha de producto (criterios 1, 2 y
  4). Las filas anteriores dicen _Sin registro_, y en la base local quedaron todas nulas.
- **Falta el criterio 3** (las marcas del recorrido del pedido): hoy no existe ninguna pantalla
  de administración de pedidos donde mostrarlas. `Orders` ya guarda `CreatedAt`; confirmado,
  listo, entregado y "preparado por" los agrega cada story de la barra junto con su
  transición, con el mismo mecanismo.
- **Sin renombrar ni borrar:** las categorías no se editan todavía, así que sus columnas
  `LastModified*` quedan nulas hasta que exista esa pantalla.

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
   se ve normal; **a los 5** pasa a ámbar y **a los 10** pasa a rojo, sin
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
- **El criterio 2 cambió el 2026-09-28:** decía que a los 10 minutos la tarjeta "pasa a rojo y
  dice *urgente*". La palabra se sacó: el rojo de la tarjeta ya lo dice, y el texto era ruido.
  Los umbrales y los colores quedan igual.
- **Borde de multi-tenancy:** el local sale del token de la estación, **nunca** de la URL.
  Esta pantalla necesita su test de aislamiento igual que el resto.
- **Borde:** la antigüedad de "Nuevos" se cuenta **desde que se pagó**, no desde que se armó el carrito
  — si no, un carrito abandonado a las 23:00 entra a la cola en rojo.

### US-16 · Tomar un pedido y sacarlo de la cola

> **Como** estación de barra
> **quiero** tomar uno o varios pedidos y que desaparezcan de la cola de nuevos
> **para** saber qué ya está en marcha y no preparar el mismo trago dos veces.

**Criterios de aceptación**

1. **Dado** un pedido en la cola, **cuando** lo tomo con "Preparar", **entonces** pasa a la
   columna de preparación y deja de aparecer entre los nuevos.
2. **Dado** que elegí varios pedidos para prepararlos juntos, **cuando** los tomo,
   **entonces** cada uno queda tomado por separado, con su propio número, y no sale uno
   combinado.
3. **Dado** un pedido que ya tomé, **cuando** lo vuelvo a tomar —un doble toque, o un
   reintento por mala señal—, **entonces** sigue tomado una sola vez y no aparece ningún error.
4. **Dado** que tomé un pedido por error, **cuando** uso "Devolver a la cola" en su tarjeta,
   **entonces** vuelve a los nuevos conservando su antigüedad original.

**Notas**

- **Depende de** US-15.
- **El botón se llama "Preparar", no "Imprimir"** (decidido el 2026-09-28): este sprint no sale
  papel, y el nombre dice lo que hace. Sólo toma el pedido; no se muestra
  un ticket en pantalla. El ticket con su QR sale el día que haya impresora (ver _Deuda que
  arrastramos_): cuando llegue, el botón es el mismo y sólo cambia que además sale el papel.
- **El QR del ticket lleva el `TrackingToken`**, el mismo que muestra el cliente para retirar
  (§10). Decidido el 2026-09-28; el porqué está en §10.
- **El criterio 3 cambió el 2026-09-28.** Decía "dos estaciones con la misma cola", pero
  cada barra va a ver sólo sus pedidos (US-33) y no hay dos KDS en una barra. Lo que sí pasa
  es el doble toque, y lo cubre la transición del dominio: tomar un pedido ya tomado no hace
  nada.
- **Elegir varios:** tocar cualquier parte de la tarjeta la elige, y abajo aparece cuántos
  pedidos hay elegidos con **todos sus tragos sumados** ("2 pedidos elegidos · 4× Gin Tonic ·
  1× Fernet con Coca"). Cada pedido se toma con su propio pedido a la API, así que si uno
  falla los demás quedan tomados igual.
- **Cada columna scrollea sola** (decidido el 2026-09-28): el tablero ocupa la pantalla justa,
  el header y la zona de avisos quedan fijos, y bajar por "Nuevos" no mueve "En preparación" ni
  "Listos". Un error de "Preparar" aparece en esa zona fija, a la vista aunque la cola sea larga.
  Cuando una columna se bajó más de una pantalla, aparece abajo de ella "↑ Volver arriba", que la
  lleva de nuevo a los pedidos más viejos sin mover las otras.
- **Se puede elegir cualquier pedido de "Nuevos"** (decidido el 2026-09-28, contra el "próximos
  10" de §11). Con el límite, las tarjetas de más abajo no respondían al toque y nada en pantalla
  decía por qué: parecía un error. Además ya se podían "Preparar" solas, así que el límite sólo
  frenaba el elegirlas junto con otras.
- **Diseño:** `KdsSeleccion.dc.html`. Quedan afuera, anotados en la deuda: `KdsDetalle`, la
  sugerencia de agrupar ("2 pedidos con Gin Tonic entre los próximos") y el "impreso hace N
  min" de la columna de preparación.

### US-18 · Marcar el pedido como listo

> **Como** estación de barra
> **quiero** marcar que el trago ya está hecho, de la forma que tenga a mano
> **para** que al cliente le llegue el aviso sin que yo pelee con la tablet.

**Criterios de aceptación**

1. **Dado** un pedido en preparación, **cuando** toco "Listo" en su tarjeta, **entonces**
   pasa a la columna de listos y el cliente lo ve en su seguimiento.
2. **Dado** que preparé varios pedidos juntos, **cuando** los elijo en "En preparación" y toco
   "Marcar listos", **entonces** cada uno pasa a listo por separado.
3. **Dado** que no tengo cámara ni lector, **cuando** busco el pedido por su número o por el
   nombre del cliente, **entonces** lo encuentro —aunque esté fuera de la vista— y lo marco
   listo igual.
4. **Dado** que toco "Listo" en una tarjeta vieja —el pedido ya cambió en otra tablet, se
   entregó o se canceló—, **entonces** el sistema me lo dice, no cambia nada y el tablero se
   pone al día.

**Notas**

- **Depende de** US-16.
- **Rehecha el 2026-09-28**, después de debatir los flujos:
  - **El escaneo sale de esta story.** No hay ticket impreso que escanear, así que la
    cámara llega con US-20 (el cliente ve su QR) y su propia pantalla de escaneo: "un
    escaneo, sin modo" como el wireframe `KdsEscanear` — en preparación pasa a Listo, listo
    pasa a Entregado. Los errores por código escaneado o tipeado (otro local, desconocido,
    entregado, cancelado) van con ese escaneo, que es donde existe el código.
    **Cómo quedó en US-20 (2026-09-29):** mientras no haya ticket impreso, el escaneo **sólo
    entrega** —en preparación avisa "todavía no está listo"—, y un QR de otro local recibe el
    mismo aviso que uno desconocido.
  - **El buscador del criterio 3 se mudó a la pantalla de escaneo** (2026-09-29, US-20). El
    tablero ya no busca: tiene un botón "Escanear o buscar", y el "+N más viejos" de Listos
    remite ahí.
  - **El aviso al cliente es su seguimiento**, que ya se consulta cada tres segundos. El push
    al celular es US-21, que escucha el evento `OrderReady` que esta story ya levanta.
  - **Sólo desde "En preparación".** Un pedido nuevo se prepara primero.
  - **"Volver a preparación"** deshace un "Listo" por error, como "Devolver a la cola".
  - **"Listos en la barra"** cuenta los minutos desde que quedó listo, sin color. Se ven
    los **10 más recientes**; los que más esperan salen de la vista (siguen listos, el número
    de la columna los cuenta y el buscador los encuentra) y la columna avisa "+N más viejos".
  - **El reloj de cada columna arranca cuando el pedido entra en ella** (2026-09-28):
    "Nuevos" cuenta desde el pago (devolverlo a la cola conserva su antigüedad), "En
    preparación" desde que se tomó —con ámbar a los 5 minutos y rojo a los 10— y "Listos"
    desde que quedó listo. Cada columna se ordena por su propio reloj. La tarjeta muestra sólo
    los minutos.
  - **Ese reloj es la auditoría del pedido.** `Order` pasó a tener la auditoría completa
    (`LastModifiedAt`, `LastModifiedBy`), que sólo escriben los cambios de estado de la barra:
    así además queda qué cuenta de barra movió cada pedido. Un doble toque no guarda nada y no
    reinicia el reloj. Si más adelante otra cosa edita un pedido sin moverlo de etapa, también
    reiniciaría el reloj: es el costo aceptado de no tener un campo propio. Ya pasa con
    **"Deshacer" una entrega**: el pedido vuelve a "Listos" contando desde cero y se ordena como
    el más nuevo, aunque haya esperado 20 minutos (aceptado en el code review del 2026-09-28).
  - **Se elige en una sola columna a la vez**: la barra de abajo ofrece "Preparar N" o
    "Marcar listos N", nunca las dos.
  - **"Entregado" a mano se adelantó de US-19**, para cuando no se puede escanear: entrega
    directo y durante 10 segundos ofrece "Deshacer". El dominio acepta deshacer hasta 30
    segundos después (margen para una red lenta); pasado eso, entregado es final. Un pedido
    entregado sin hora registrada —anterior a la columna— no se puede deshacer.

### US-19 · Entregar el pedido en la barra

> **Estado al 2026-09-29: ✅ terminada.** El **criterio 2 quedó hecho en US-18** ("Entregado" en
> la tarjeta, con "Deshacer"; el buscador después se mudó a la pantalla de escaneo). Los
> **criterios 1, 3 y 4 se hicieron en US-20**, con la pantalla de escaneo.

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
  es de quien lo reclama.
- **"Entregado" va suelto en la tarjeta de "Listos"** (decidido el 2026-09-28, contra lo que
  proponía esta nota: ponerlo detrás de buscar el pedido). La red es "Deshacer" durante unos
  segundos, y el camino principal sigue siendo el QR.
- **El escaneo es una acción aparte de los botones** (2026-09-28). Los botones toleran el doble
  toque —entregar algo ya entregado no hace nada—; el escaneo, en cambio, avisa lo que encontró:
  "no está listo", "ya se entregó" (criterio 4: dos personas reclamando el mismo número) o "no
  es de este local".
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

### US-31 · Que el tablero se ponga al día solo

> **Como** estación de barra
> **quiero** que la cola se actualice sola cada tanto, aunque no llegue ningún aviso
> **para** que un pedido pagado no quede fuera de la tablet porque se perdió una notificación.

**Criterios de aceptación**

1. **Dado** un pedido pagado cuyo aviso al tablero no llegó, **cuando** pasa el intervalo de
   respaldo, **entonces** el pedido aparece en la cola sin que nadie toque la tablet.
2. **Dado** que el tablero está recibiendo avisos con normalidad, **cuando** pasa el
   intervalo, **entonces** la recarga no hace parpadear las tarjetas ni muestra "Cargando".

**Notas**

- **Depende de** US-15.
- **De dónde sale:** hoy el tablero sólo recarga cuando llega un aviso por SignalR o cuando
  se reconecta. Si el aviso falla después de guardado el pedido, el pedido existe pero la
  barra no lo ve hasta que llegue **otro**. Ya quedó en el log (`OrderRepository`) y el
  despacho ya no se cancela si el cliente cierra la app, pero nada lo recupera solo.
- **A decidir antes de tomarla:** cada cuánto. Se habló de 30 o 60 segundos: es una GET
  liviana por tablet, y el peor caso pasa a ser ese atraso en vez de un pedido invisible.

### US-32 · Que el tablero me mande a entrar si se venció

> **Como** estación de barra
> **quiero** que, si mi sesión se venció, el tablero me lleve a la pantalla de ingreso
> **para** no quedarme mirando una cola vieja que dice "reintentando" para siempre.

**Estado:** ✅ terminada el 2026-09-28, en la rama de deuda antes de US-18.

**Criterios de aceptación**

1. **Dado** que la sesión de la tablet se venció, **cuando** el tablero pide la cola,
   **entonces** me lleva a la pantalla de ingreso avisando que la sesión terminó.
2. **Dado** que la sesión se venció y se cortó la conexión en vivo, **cuando** el tablero
   intenta reconectarse, **entonces** me lleva a la misma pantalla en vez de reintentar sin
   fin.

**Notas**

- **Depende de** US-15.
- **De dónde sale:** el token dura 8 horas y una noche de boliche puede durar más. Cuando la
  API contesta 401, el interceptor borra la sesión, pero **nadie navega al login**: el
  guard sólo corre al cambiar de ruta, y en la tablet nadie cambia de ruta. Además, la
  conexión de SignalR no pasa por el interceptor, así que un 401 al reconectar se reintenta
  cada 5 segundos para siempre.
- **Alcance decidido:** sólo el tablero. Las pantallas de administración tienen el mismo
  hueco, pero ahí alguien termina navegando; llevarlo a toda la app es otra conversación.

### US-33 · Repartir los pedidos entre las barras

> **Como** boliche con más de una barra
> **quiero** que cada pedido pagado vaya a una sola barra, a la que tenga menos trabajo
> **para** que ninguna barra se sature mientras otra está libre, y que dos barras nunca
> preparen el mismo pedido.

**Criterios de aceptación**

1. **Dado** un boliche con dos barras, **cuando** se paga un pedido, **entonces** aparece en
   la cola de una sola de ellas.
2. **Dado** que una barra tiene más pedidos esperando que otra, **cuando** se paga uno nuevo,
   **entonces** va a la que tiene menos.
3. **Dado** que el pedido cayó en una barra, **cuando** el cliente mira su seguimiento,
   **entonces** sabe en qué barra lo retira.

**Notas**

- **Sale de US-16**, decidido el 2026-09-28: cada KDS ve sólo los pedidos de su barra. Por eso
  el criterio 3 de US-16 dejó de ser una carrera entre estaciones.
- **Preguntas abiertas antes de tomarla:**
  - Qué cuenta como "carga": pedidos en `Queued`, en `Queued` + `InPreparation`, o tragos
    en vez de pedidos.
  - Cómo se ata una cuenta KDS a su `BarStation` (hoy la cuenta no sabe de qué barra es) y
    quién da de alta las barras.
  - Qué pasa con los pedidos de una barra que se cierra en medio de la noche.
- **Borde:** con una sola barra, nada cambia respecto de hoy. Tiene que seguir funcionando
  sin configurar nada.
- **Concurrencia (code review de US-18, 2026-09-28):** `Order` no tiene token de concurrencia,
  así que dos tablets moviendo el mismo pedido a la vez se pisan (por ejemplo, "Entregado" y
  "Volver a preparación" juntos dejan un pedido en preparación con fecha de entrega). Hoy no
  pasa porque hay **una sola tablet por barra**, y con este reparto cada pedido llega a una sola
  tablet. Si alguna vez dos tablets comparten una cola, hace falta el token.

## El cliente

### US-20 · Que el cliente vea su QR de retiro

**Estado:** ✅ hecha el 2026-09-29. Falta la **prueba a mano del criterio 2**: un celular real
con el brillo al mínimo contra la tablet de la barra.

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
  **Ese identificador es el `TrackingToken`** (decidido en US-16, el 2026-09-28): así sólo
  retira quien tiene el celular. El número del pedido se sigue mostrando al lado, para
  leerlo, pero no alcanza para retirar.
- **Dependencia aprobada:** `angularx-qrcode` (ver _Dependencias nuevas_).
- **Suma lo que quedó de US-19 (2026-09-28):** con el QR a la vista, se hace la **pantalla de
  escaneo** como el wireframe `KdsEscanear` —cámara, "Últimos escaneos" y búsqueda manual—, a la
  que se llega desde el tablero. Un escaneo, sin modo: en preparación pasa a Listo, listo pasa
  a Entregado, y cualquier otro caso avisa sin cambiar nada. Cierra los criterios 1, 3 y 4 de
  US-19. Usa `barcode-detector` y `zxing-wasm` (aprobadas el 2026-09-23).
- **Cómo quedó, decidido al hacerla (2026-09-29):**
  - **El escaneo sólo entrega.** Sin ticket impreso, el único QR que existe es el del cliente,
    y mostrarlo antes de tiempo no puede marcar listo un trago sin hacer. En la cola o en
    preparación avisa "Todavía no está listo" y no cambia nada. El paso a Listo llega con la
    impresora.
  - **El QR lleva sólo el token**, ni el link ni el número: un celular que lo enfoque no abre
    nada, y un lector que tipea como teclado no rompe un `:` ni un `/`. No es secreto para quien
    fotografía toda la pantalla —el número está impreso arriba—, y por eso sólo sirve para
    retirar en una barra con alguien mirando.
  - **Otro local, un token que no existe o algo que no es un token** dan el mismo aviso: "No
    reconocemos este código". Distinguir "otro local" exigiría consultar saltando el filtro de
    tenant.
  - **Se lee de dos formas que terminan en lo mismo:** un lector USB o Bluetooth que tipea en un
    campo con foco (§10), y la cámara, **a pedido**: el bloque se ve como el wireframe, y el botón
    "Usar la cámara" la abre para un cliente. Se cierra al leer, a los 30 segundos sin leer nada
    o con "Cancelar".
  - **Cada escaneo enviado hace destellar el bloque**, igual para todos los resultados; el
    resultado se lee en el texto. Con "reducir movimiento" se ilumina el borde en vez de
    destellar.
  - **"Últimos escaneos"** muestra los cinco más recientes, sólo en memoria de la tablet y
    nunca con el token. La misma lectura repetida dentro de tres segundos se ignora: la cámara
    ve el mismo QR en cada cuadro.
  - **"Deshacer" también después de escanear**, durante 10 segundos, como el botón manual.
  - **Si se corta la red** no sabemos si el escaneo llegó, y la pantalla lo dice así: "No
    sabemos si se registró. Escaneá de nuevo: si dice 'Ya se entregó', fue este escaneo". Decir
    "sin conexión" a secas hacía que se rechazara al cliente al que ya se le había entregado.
  - **La búsqueda manual se mudó del tablero** a esta pantalla, con "Listo", "Entregado" y
    "Deshacer". Si el lector tipea en el buscador, se envía como escaneo y el token no queda en
    pantalla.
- **Deuda del code review (2026-09-29)**, anotada y no arreglada:
  - **Dos escaneos simultáneos del mismo pedido listo contestan "Entregado" los dos.** Es la
    falta de token de concurrencia en `Order` que ya anota US-33; hoy no pasa porque hay una
    sola tablet por barra.
  - **El escaneo de un pedido cancelado o sin pagar diría "Todavía no está listo".** Hoy no hay
    transición a esos estados; cuando lleguen US-23 y US-25, el escaneo necesita su propio aviso
    para cada uno.
  - **Los colores del QR repiten a mano `--dk-paper` y `--dk-on-paper`**, porque la librería
    los pide como valores y no como CSS. Si cambia el token, hay que cambiar las constantes de
    `tracking.page.ts`.

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

**Construida el 2026-09-28**, junto con US-25 y US-26 en la rama
`feat/24-pago-en-efectivo-y-caja`:

- `CashPaymentStrategy` deja el pedido en `AwaitingPayment` con el método guardado y sin
  `PaidAt`. El handler de confirmar no cambió: es la clase nueva que prometía la nota.
- El checkout ofrece digital y efectivo; el saldo VIP sigue dibujado y apagado. Con efectivo
  el botón dice "Confirmar pedido", no "Pagar": en el celular no se paga nada.
- **Deuda saldada:** el `method` del pedido viaja como enum del contrato (`PaymentMethodName`)
  y es obligatorio. Un nombre desconocido o un campo que falta se rechazan con 400 al leer el
  body, igual que el resto de los enums; desapareció `TryReadPaymentMethod`.
- **El stock se descuenta al confirmar, no al cobrar.** Un pedido en efectivo que nadie paga
  retiene su stock hasta que se lo cancele, y cancelar es US-23. Aceptado para este sprint.

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

**Construida el 2026-09-28.** Es la misma pantalla de seguimiento: con `AwaitingPayment`
manda a la caja con el código y dice que falta pagar, sin ningún paso del recorrido marcado.
El criterio 3 sale de la consulta cada tres segundos que ya existía; cuando llegue US-22 lo
hereda. Quedan afuera del wireframe `Efectivo`: el aviso push (US-21), el QR (US-20) y el
botón "Cancelar pedido" del cliente, que ningún criterio pide.

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

**Construida el 2026-09-28.** Decidido ese día:

- **Rol `Cashier`** (número 4) con su política, su guard y su pantalla en
  `/:venueSlug/staff/cashier`. El administrador ya lo puede dar de alta.
- **Sólo se busca por código.** El escaneo espera a US-20, que es la que dibuja el QR; la
  búsqueda por nombre de `CajeroBuscar` queda como deuda. El buscador envía con Enter, así que
  un lector USB funciona el día que se compre.
- **"Mi lista de pendientes" es real:** debajo del buscador, los pedidos en `AwaitingPayment`
  del local, el más viejo primero. Tocar uno lo abre para cobrar sin tipear.
- **Cobrar dos veces se rechaza con 409** y la pantalla dice "ya está pago". Buscar un pedido
  ya cobrado dice lo mismo, con la hora. Nunca se cobra dos veces.
- ~~**La lista no se actualiza sola.**~~ Resuelto el 2026-09-30, ver abajo.

**Rediseñada el 2026-09-29**, sobre los wireframes y después de traer US-20 (QR) de `dev`:

- **Desktop primero y responsive.** A la izquierda el trabajo —escanear o cobrar—, a la derecha
  las dos listas: **Por cobrar** y **Cobros de tu turno**, con el total cobrado. Debajo de
  1024px todo se apila, el trabajo primero.
- **El escaneo es un botón, no el bloque grande de `CajeroBuscar`.** Un solo campo recibe las
  tres entradas: el lector USB escribe el QR y manda Enter, el cajero tipea el código, y el botón
  "Escanear con la cámara" abre la misma cámara del KDS (ahora `shared/qr-camera`). Una lectura
  de 32 hex es un token y va a `POST /cashier/scan` en el body; cualquier otra cosa es un código.
- **El QR se muestra apenas se confirma en efectivo** (antes, US-20 lo mostraba sólo para
  retirar). Es el mismo token, así que el cajero lo escanea igual que la barra.
- **Cobrar es de dominio:** `Order.CollectCash` paga, guarda quién cobró (`CollectedBy`, con su
  migración) y pasa a la cola. `Pay` quedó sólo para el pago desde el celular.
- **El turno son las últimas 12 horas del cajero logueado**, no "hoy": una noche de boliche
  cruza la medianoche. Si algún día hay cierre de caja, se vuelve una entidad `Shift`.
- **Sin solapas.** "Entregas VIP" no es de este sprint.
- **Header compartido:** `StaffHeader` (venue, contexto, cuenta con "Salir", tema y solapas
  opcionales). La administración lo usa con sus dos solapas y la caja sin ninguna.
- Sigue en deuda la búsqueda por nombre de `CajeroBuscar`.
- **"Por cobrar" se actualiza sola** (2026-09-30), con el mismo mecanismo que el tablero de la
  barra. Confirmar en efectivo levanta `OrderAwaitingPayment` y cobrar levanta `OrderCollected`
  (además de `OrderQueued`, que es de la barra). El dispatcher los manda a `ITillNotifier`, que
  avisa por un hub propio, `TillHub` en `/hubs/till`, sólo para `Cashier` y con un grupo por
  local sacado del token. La pantalla recarga la lista con cada aviso y al volver de un corte, y
  avisa si está desconectada. El front comparte la lógica de conexión con el KDS
  (`core/realtime/hub-channel.ts`).
- **Una carrera en los tests de los hubs:** el cliente de SignalR termina `StartAsync` antes de
  que el hub meta la conexión en su grupo, y un aviso enviado en ese instante se pierde. Los
  tests de aislamiento de los dos hubs ahora reenvían hasta escucharlo. En la app no se nota:
  la pantalla carga su lista al abrir.
- **El seguimiento dice "En cola", no "Esperando en la barra"** (decidido el 2026-09-30): la
  frase vieja se leía como "tu trago ya te espera en la barra". El paso es **En cola**, y la frase
  de un pedido pago pasa a ser "Ya está pago y en la cola. Te avisamos cuando lo empiecen a
  preparar." El criterio 5 de US-11 decía lo anterior; queda como estaba en el backlog del
  Sprint 1, que es historia.

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

**Nueva, de US-16: lo que quedó afuera del wireframe del KDS.** La pantalla de detalle
(`KdsDetalle`), la sugerencia de agrupar pedidos del mismo trago y el "impreso hace N min" de la
columna de preparación, que necesita guardar la hora en que se tomó (columna nueva y
migración). Ningún criterio los pide.

**Nueva, de US-16: la impresora ya no es sólo un papel.** "Preparar" hoy sólo toma el pedido.
Cuando llegue la impresora, además del papel, el ticket tiene que llevar el QR con el
`TrackingToken`, que es lo que escanea la pantalla de US-20. Ese día el escaneo suma el paso a
Listo: hoy sólo entrega, porque el único QR que existe es el del cliente.

**Nueva, de auditar US-15: el tablero.** Dos huecos que ya tienen story propia, US-31 (se
pone al día solo si se pierde un aviso) y US-32 (manda al login si la sesión se venció).
**US-32 quedó hecha el 2026-09-28; US-31 sigue pendiente.**

**Nueva, de auditar US-15: los enums del contrato viajan como `string`.** Los siete —`role`
en el login, en el personal y en sus altas y cambios, y `status` en el pedido confirmado, en
el seguimiento y en la cola del KDS— salen con `.ToString()`, así que el `schema.d.ts`
generado dice `string` y el front compara contra literales que nada controla. Se arregla
publicándolos como enum de OpenAPI **en los siete a la vez**: hacerlo en uno solo deja el
contrato con dos convenciones.

**Resuelta el 2026-09-28.** La API tiene sus propios enums (`StaffRoleName`,
`CustomerOrderStatus`, `KdsOrderStatus`), mapeados a mano desde el dominio para que el
contrato no publique estados internos como `Cart`. Viajan sólo por su nombre: un número o una
lista con coma (`"Administrator,Kds"`, que el lector de .NET convertía en otro rol válido) se
rechazan con 400. En el camino apareció que **un body que no se podía leer respondía 500**, en
cualquier endpoint: ahora es 400.

**Nueva, de resolver los enums: la forma de pago también viaja como `string`.** El `method`
del pedido confirmado es un octavo enum que la cuenta de arriba no tenía. Sigue validado a
mano con `TryReadPaymentMethod`; pasarlo a `PaymentMethodName` es el mismo trabajo, cuando
llegue el carril del pago (US-24).
**Resuelta el 2026-09-28, con US-24.**

## Dependencias nuevas

Aprobadas el **2026-09-23**. Las tres son **MIT** y están vivas — se verificó contra el
registro de npm el mismo día, no contra lo que decía un tutorial.

| Paquete            | Versión | Licencia | Última publicación | Para qué                                     |
| ------------------ | ------- | -------- | ------------------ | -------------------------------------------- |
| `angularx-qrcode`  | 22.0.1  | MIT      | 2026-07-24         | Dibujar el QR del cliente y el del ticket    |
| `barcode-detector` | 3.2.2   | MIT      | 2026-08-16         | Leerlo donde el navegador no sabe            |
| `zxing-wasm`       | 3.1.3   | MIT      | 2026-09-10         | Lo trae `barcode-detector`, no se instala solo |

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

**Instaladas el 2026-09-29, con US-20.** Dos cambios respecto de lo aprobado:

- **`zxing-wasm` es la 3.1.3, no la 3.1.4.** Es la que fija `barcode-detector` 3.2.2; la tabla
  de arriba ya lo dice.
- **`zxing-wasm` sí se instala sola, fijada en 3.1.3.** Por defecto la librería baja su `.wasm`
  de jsDelivr; nosotros lo servimos desde nuestros propios assets (`/zxing/`), para que una red
  de boliche que filtre ese CDN no deje ciega a la cámara. Angular sólo lo puede copiar si el
  paquete figura en `package.json`. El riesgo es que se desalineen las dos versiones al
  actualizar una: `qr-reader.spec.ts` falla si la que fija `barcode-detector` y la nuestra no
  coinciden.

Se usa el **ponyfill** y no el polyfill: no se parchea `window`. Donde el navegador trae
`BarcodeDetector` y sabe leer QR se usa el nativo; si no, se carga el de la librería, en un
chunk aparte.

**Aprobada el 2026-09-28, con el PR #18 (US-15): `@microsoft/signalr`.** Entró con el
tablero sin figurar en esta lista; queda escrita acá para que la lista diga la verdad.

| Paquete              | Versión | Licencia | Última publicación | Para qué                                          |
| -------------------- | ------- | -------- | ------------------ | ------------------------------------------------- |
| `@microsoft/signalr` | 10.0.11 | MIT      | 2026-08-04         | Que el tablero del KDS escuche la cola en vivo    |

Es el cliente oficial de Microsoft para el hub que ya vive en el backend (SignalR es parte
del framework de ASP.NET Core, no una dependencia aparte). La licencia se verificó contra el
registro de npm el 2026-09-28.
