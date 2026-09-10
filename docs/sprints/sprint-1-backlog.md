# Backlog del Sprint 1

Lo que nos comprometemos a entregar de la [consigna del Sprint 1](sprint-1.md).
**Sprint: lunes 7 → jueves 17 de septiembre de 2026.** Equipo de tres, part-time.

## Cómo escribimos las stories

Una story describe **qué necesita una persona y para qué le sirve**, nunca cómo se construye.
El "cómo" es de quien la implementa y no se negocia en la planificación.

Formato:

> **Como** <rol concreto> · **quiero** <capacidad> · **para** <valor que obtiene>

Tres reglas que nos aplicamos:

- **El rol es una persona real** (cliente, administrador, bartender), nunca "usuario".
- **El "para" tiene que aportar algo.** Si dice "para poder verlo", está de más: eso ya lo
  dijo el "quiero". El "para" es lo que se pierde si la story no se hace.
- **Los criterios de aceptación se demuestran apretando botones.** Van en Dado / Cuando /
  Entonces, son binarios y no mencionan tecnología. Si hace falta abrir la base de datos para
  verificar uno, está mal escrito.

**Estimación**: puntos de historia en Fibonacci, comparando una story contra otra. Es el
primer sprint y **no tenemos velocidad histórica**, así que los puntos sirven para ver qué es
más grande que qué, no para prometer que entra todo. El control real es el hito del domingo 13.

**Prioridad**: `Imprescindible` es lo que pide la consigna textualmente. `Debería` es lo que
hace que la entrega se sostenga sola, y es lo primero que se recorta si llegamos justos.

**Lista para tomar** (Definition of Ready): tiene narrativa, criterios de aceptación, está
estimada y no depende de nada sin terminar. Si le falta algo, no entra al sprint.

## Quiénes usan el sistema en este sprint

| Rol | Qué hace | Se autentica |
|---|---|---|
| **Cliente** | Pide tragos desde su celular | No. No se registra ni instala nada |
| **Administrador** | Carga la carta y al personal de su local | Sí |
| **Bartender** | Prepara los pedidos y avisa cuándo están listos | Sí |

## Resumen

| ID | Story | Prioridad | Puntos | Depende de | Estado |
|---|---|---|---|---|---|
| US-01 | Iniciar sesión | Imprescindible | 5 | — | **En curso** — backend listo, falta pantalla y roles |
| US-02 | Poder entrar la primera vez | Imprescindible | 2 | — | **En curso** — semilla lista, falta el listado |
| US-03 | Dar de alta al equipo | Imprescindible | 3 | US-01 | Pendiente |
| US-04 | Ver y corregir al equipo | Imprescindible | 3 | US-03 | Pendiente |
| US-05 | Dar de baja a quien se fue | Imprescindible | 2 | US-03 | Pendiente |
| US-06 | Cargar un trago en la carta | Imprescindible | 3 | US-01 | Pendiente |
| US-07 | Marcar que un trago se acabó | Imprescindible | 2 | US-06 | Pendiente |
| US-08 | Corregir y sacar tragos | Imprescindible | 3 | US-06 | Pendiente |
| US-09 | Ver la carta desde el celular | Imprescindible | 3 | US-06 | Pendiente |
| US-10 | Armar el pedido | Imprescindible | 5 | US-09 | Pendiente |
| US-11 | Confirmar el pedido | Imprescindible | 5 | US-10 | Pendiente |
| US-12 | Seguir mi pedido | Imprescindible | 5 | US-11 | Pendiente |
| US-13 | Ver qué hay para preparar | Debería | 5 | US-11 | Pendiente |
| US-14 | Avisar que el pedido avanza | Debería | 3 | US-13 | Pendiente |

**49 puntos**, de los cuales 8 son `Debería`. Sin velocidad histórica ese número no dice si
entra: por eso los dos `Debería` están al final y son el margen.

---

## Arranque

### US-01 · Iniciar sesión

> **Como** administrador o bartender de un local
> **quiero** entrar con mi usuario y contraseña
> **para** trabajar sobre los datos de mi local sin que nadie pueda operar en mi nombre.

`Imprescindible` · `5 puntos` · sin dependencias

1. **Dado** que soy un bartender activo, **cuando** ingreso mi usuario y contraseña
   correctos, **entonces** entro y veo la cola de pedidos de mi local.
2. **Dado** que soy un administrador activo, **cuando** ingreso correctamente, **entonces**
   entro y veo las pantallas de gestión.
3. **Dado** que escribo mal el usuario, **y dado** que otra vez escribo mal la contraseña,
   **entonces** en los dos casos recibo exactamente el mismo mensaje, que no revela cuál de
   los dos estaba mal.
4. **Dado** que me dieron de baja, **cuando** ingreso mi contraseña correcta, **entonces** no
   me deja entrar.
5. **Dado** que entré como bartender, **cuando** escribo a mano la dirección de una pantalla
   de administración, **entonces** el sistema no me la muestra.
6. **Dado** que entré en un local, **cuando** cambio la dirección del navegador apuntando a
   otro local, **entonces** no veo ni un dato de ese otro local.

> **Nota:** los criterios 3 y 4 son deliberados, no una omisión de usabilidad. Un mensaje que
> distinga "ese usuario no existe" de "la contraseña está mal" le permite a cualquiera
> averiguar quién trabaja en el local probando nombres.

**Avance — 3 de 6.** El backend está terminado y con tests; falta la pantalla y la
autorización por rol.

| Criterio | Estado |
|---|---|
| 1. Bartender entra y ve la cola | Falta la pantalla |
| 2. Administrador entra y ve la gestión | Falta la pantalla |
| 3. Mismo mensaje para usuario y para contraseña | ✅ `LoginHandlerTests` |
| 4. Dado de baja no entra | ✅ `LoginHandlerTests` |
| 5. Bartender no accede a pantallas de administración | Falta autorización por rol |
| 6. Cambiar el local en la dirección no revela nada | ✅ `VenueIsolationTests` |

> El criterio 5 **no es sólo frontend**: hoy no existe autorización por rol en el backend y
> ningún endpoint usa `RequireAuthorization`. Esconder un botón no cumple el criterio, porque
> dice "escribo a mano la dirección".

### US-02 · Poder entrar la primera vez

> **Como** administrador de un local nuevo
> **quiero** que mi cuenta ya exista cuando el sistema se pone en marcha
> **para** no quedar afuera de un sistema donde las cuentas sólo las crea un administrador.

`Imprescindible` · `2 puntos` · sin dependencias

1. **Dado** un sistema recién puesto en marcha, **cuando** abro la pantalla de ingreso,
   **entonces** existe un local y un administrador con el que puedo entrar.
2. **Dado** que entré con esa cuenta, **cuando** miro el listado de personal, **entonces**
   soy la única persona cargada.

> **Nota:** es un habilitador, pero tiene valor de usuario propio y por eso es una story y no
> una tarea escondida: sin ella nadie puede usar el sistema el primer día.

**Avance — 1 de 2.** El criterio 1 está cubierto por `DevelopmentSeederTests`: al arrancar
existen un local y un administrador. El criterio 2 necesita el listado de personal, que llega
con US-04.

---

## Personal del local

### US-03 · Dar de alta al equipo

> **Como** administrador
> **quiero** dar de alta a quienes trabajan conmigo indicando su rol
> **para** que cada uno acceda solamente a la parte del sistema que necesita.

`Imprescindible` · `3 puntos` · depende de US-01

1. **Dado** que completo usuario, contraseña y rol, **cuando** guardo, **entonces** la
   persona aparece en el listado y puede iniciar sesión enseguida.
2. **Dado** que elijo el rol, **cuando** abro las opciones, **entonces** puedo elegir entre
   administrador y bartender.
3. **Dado** que ese nombre de usuario ya existe en mi local, **cuando** intento guardar,
   **entonces** me avisa y no se crea un duplicado.
4. **Dado** que otro boliche tiene un usuario con ese mismo nombre, **cuando** lo cargo en el
   mío, **entonces** se crea sin problema.

### US-04 · Ver y corregir al equipo

> **Como** administrador
> **quiero** ver a mi personal y poder corregir sus datos
> **para** arreglar un rol mal asignado o una contraseña olvidada sin tener que borrar y
> volver a cargar a la persona.

`Imprescindible` · `3 puntos` · depende de US-03

1. **Dado** que abro el listado, **cuando** lo miro, **entonces** veo de cada persona su
   usuario, su rol y si está activa.
2. **Dado** que alguien pasó de bartender a encargado, **cuando** le cambio el rol y guardo,
   **entonces** la próxima vez que entra ve las pantallas del rol nuevo.
3. **Dado** que alguien olvidó su contraseña, **cuando** le cargo una nueva, **entonces**
   puede entrar con esa y no con la anterior.
4. **Dado** que hay personal cargado en otro local, **cuando** abro mi listado, **entonces**
   no aparece.

### US-05 · Dar de baja a quien se fue

> **Como** administrador
> **quiero** dar de baja a quien dejó de trabajar acá
> **para** cerrarle el acceso sin perder el registro de los pedidos que preparó.

`Imprescindible` · `2 puntos` · depende de US-03

1. **Dado** que doy de baja a un bartender, **cuando** intenta iniciar sesión, **entonces**
   no puede entrar.
2. **Dado** que lo di de baja, **cuando** miro el listado, **entonces** sigue estando, marcado
   como inactivo.
3. **Dado** que preparó pedidos antes de la baja, **cuando** consulto esos pedidos,
   **entonces** siguen mostrando quién los preparó.
4. **Dado** que esa persona vuelve a trabajar, **cuando** la reactivo, **entonces** entra con
   su cuenta de siempre, sin cargarla de nuevo.

---

## La carta

### US-06 · Cargar un trago en la carta

> **Como** administrador
> **quiero** cargar un trago con nombre, descripción, foto y precio
> **para** que el cliente elija sabiendo qué es y cuánto sale, sin preguntarle a nadie.

`Imprescindible` · `3 puntos` · depende de US-01

1. **Dado** que completo nombre, descripción, foto y precio, **cuando** guardo, **entonces**
   el trago aparece en la carta que ve el cliente.
2. **Dado** que dejo el precio en cero o en negativo, **cuando** intento guardar,
   **entonces** no me deja y me dice por qué.
3. **Dado** que cargué una foto, **cuando** el cliente abre la carta, **entonces** la ve junto
   al trago.

### US-07 · Marcar que un trago se acabó

> **Como** administrador
> **quiero** marcar un trago como agotado
> **para** que nadie lo pida y lo pague cuando no se lo podemos preparar.

`Imprescindible` · `2 puntos` · depende de US-06

1. **Dado** que marco un trago como agotado, **cuando** el cliente abre la carta,
   **entonces** lo ve indicado como agotado y no lo puede agregar al pedido.
2. **Dado** que repusimos, **cuando** lo vuelvo a habilitar, **entonces** el cliente puede
   pedirlo otra vez, sin que yo haya tenido que cargarlo de nuevo.

> **Nota:** el trago agotado se muestra en vez de ocultarse a propósito. Si desapareciera de
> la carta, el cliente pensaría que se cargó mal e iría a preguntar a la barra, que es
> exactamente la caminata que el producto quiere evitar.

### US-08 · Corregir y sacar tragos

> **Como** administrador
> **quiero** editar un trago o sacarlo de la carta
> **para** mantenerla fiel a lo que vendemos hoy, sin que se me modifiquen los pedidos que ya
> se cobraron.

`Imprescindible` · `3 puntos` · depende de US-06

1. **Dado** que corrijo el nombre, la descripción, la foto o el precio, **cuando** guardo,
   **entonces** la carta del cliente muestra el dato nuevo.
2. **Dado** que un cliente confirmó un pedido ayer, **cuando** hoy le subo el precio a ese
   trago, **entonces** el pedido de ayer sigue mostrando el precio con el que se pidió.
3. **Dado** que saco un trago de la carta, **cuando** el cliente la abre, **entonces** ya no
   está.
4. **Dado** que ese trago aparecía en pedidos anteriores, **cuando** consulto esos pedidos,
   **entonces** lo siguen mostrando tal como se pidió.

---

## El pedido del cliente

### US-09 · Ver la carta desde el celular

> **Como** cliente en un boliche
> **quiero** ver la carta en mi celular sin instalar ni registrarme
> **para** decidir qué pedir desde donde estoy, en vez de hacer la fila para leer un cartel.

`Imprescindible` · `3 puntos` · depende de US-06

1. **Dado** que escaneo el QR del boliche, **cuando** se abre la página, **entonces** veo la
   carta de ese boliche.
2. **Dado** que entro por primera vez, **cuando** navego la carta, **entonces** en ningún
   momento me pide crear una cuenta, iniciar sesión ni descargar una aplicación.
3. **Dado** que la abro en un celular, **cuando** la uso con una mano, **entonces** todo lo
   que necesito tocar me queda al alcance del pulgar.
4. **Dado** que hay tragos agotados, **cuando** miro la carta, **entonces** se distinguen a
   simple vista de los disponibles.

### US-10 · Armar el pedido

> **Como** cliente
> **quiero** elegir tragos con su cantidad y dejar una aclaración en cada uno
> **para** recibir exactamente lo que quiero sin tener que gritárselo al bartender por encima
> de la música.

`Imprescindible` · `5 puntos` · depende de US-09

1. **Dado** que elijo un trago, **cuando** lo agrego, **entonces** aparece en mi pedido con el
   total actualizado.
2. **Dado** que ya lo agregué, **cuando** cambio la cantidad o lo saco, **entonces** el total
   se ajusta al instante.
3. **Dado** que quiero pedirlo de una forma particular, **cuando** escribo "sin hielo" en ese
   trago, **entonces** la aclaración queda guardada en ese trago y no en todo el pedido.
4. **Dado** que me llamó alguien por teléfono y salí de la app, **cuando** vuelvo,
   **entonces** mi pedido sigue armado como lo dejé.

### US-11 · Confirmar el pedido

> **Como** cliente
> **quiero** confirmar mi pedido dejando mi nombre
> **para** que empiecen a prepararlo y tener un número con el cual reclamarlo en la barra.

`Imprescindible` · `5 puntos` · depende de US-10

1. **Dado** que tengo tragos en mi pedido, **cuando** escribo mi nombre y confirmo,
   **entonces** el pedido queda registrado para retirar en la barra.
2. **Dado** que confirmé, **cuando** veo la respuesta, **entonces** me muestra un número de
   pedido corto, fácil de leer y de decir en voz alta.
3. **Dado** que no escribí mi nombre, **cuando** intento confirmar, **entonces** no me deja y
   me lo pide.
4. **Dado** que otro cliente confirma en el mismo boliche al mismo tiempo, **cuando** compara
   su número con el mío, **entonces** son distintos.
5. **Dado** que confirmé, **cuando** miro el estado, **entonces** el pedido ya figura pago y
   esperando en la barra.

> **Nota:** el pago está simulado en este sprint, como habilita la consigna. Por eso confirmar
> equivale a pagar.

### US-12 · Seguir mi pedido

> **Como** cliente
> **quiero** ver en qué estado está mi pedido
> **para** acercarme a la barra recién cuando está listo, en vez de esperar parado.

`Imprescindible` · `5 puntos` · depende de US-11

1. **Dado** que confirmé mi pedido, **cuando** miro la pantalla de seguimiento, **entonces**
   veo mi número y en qué anda: esperando, en preparación, listo o entregado.
2. **Dado** que el bartender lo marca listo, **cuando** tengo la pantalla abierta,
   **entonces** el estado cambia solo, sin que yo refresque.
3. **Dado** que cerré la página, **cuando** vuelvo al mismo enlace más tarde, **entonces**
   sigo viendo mi pedido y su estado actual.
4. **Dado** que alguien conoce mi número de pedido, **cuando** intenta llegar a mi pantalla de
   seguimiento probando números en la dirección, **entonces** no lo consigue.

---

## La barra

### US-13 · Ver qué hay para preparar

> **Como** bartender
> **quiero** ver los pedidos que entraron, en orden de llegada
> **para** preparar primero al que hace más rato que espera.

`Debería` · `5 puntos` · depende de US-11

1. **Dado** que entré con mi usuario, **cuando** abro la pantalla de la barra, **entonces**
   veo solamente los pedidos de mi local.
2. **Dado** que miro un pedido, **cuando** lo leo, **entonces** veo su número, el nombre del
   cliente, cada trago con su cantidad y las aclaraciones.
3. **Dado** que hay varios esperando, **cuando** miro la lista, **entonces** el que hace más
   rato que espera está primero.
4. **Dado** que entra un pedido nuevo, **cuando** tengo la pantalla abierta, **entonces**
   aparece sin que yo refresque.

### US-14 · Avisar que el pedido avanza

> **Como** bartender
> **quiero** marcar cuándo tomo un pedido, cuándo lo termino y cuándo lo entrego
> **para** que el cliente venga a buscarlo en el momento justo y no se me amontone gente en
> la barra.

`Debería` · `3 puntos` · depende de US-13

1. **Dado** que tomo un pedido, **cuando** lo marco, **entonces** el cliente ve "en
   preparación" en su pantalla.
2. **Dado** que terminé de prepararlo, **cuando** lo marco listo, **entonces** el cliente ve
   "listo".
3. **Dado** que se lo entregué, **cuando** lo marco entregado, **entonces** desaparece de mi
   pantalla y el cliente ve "entregado".
4. **Dado** un pedido que todavía no está listo, **cuando** intento marcarlo entregado,
   **entonces** el sistema no me deja saltear el paso.

> **Nota:** US-13 y US-14 no figuran en los seis puntos de la consigna, pero la consigna pide
> el rol Bartender. Sin ellas ese rol no hace nada y el pedido del cliente se queda para
> siempre en "esperando". Son `Debería` porque son el margen del sprint: si llegamos justos,
> se recorta acá. Cuesta la demo, no la consigna.

---

## Decisiones que tomamos para llegar a la fecha

En cada caso se eligió la opción más barata que cumple lo que se pide. Ninguna cierra la
puerta a la versión definitiva.

| Tema | Cómo queda en este sprint | Cómo va a ser después |
|---|---|---|
| Foto del trago | El administrador pega el enlace de una imagen | Sube el archivo desde su computadora |
| Aviso de "listo" | El cliente lo ve en su pantalla, que se actualiza sola | Además le llega una notificación al celular |
| Pago | Confirmar el pedido equivale a pagarlo | Pago digital, efectivo en caja y saldo de mesa VIP |
| Ticket de la barra | El bartender trabaja desde la pantalla | Se imprime el ticket y se escanea el QR |

## Trabajo que no es una story

Nadie lo puede "ver funcionando", así que no lleva tarjeta propia: va adentro de la primera
story que lo necesita.

- El frontend no tiene ninguna pantalla ni ruta definida. Empieza con US-01.
- Playwright no está instalado y el Definition of Done pide una prueba de punta a punta.
- No existe autorización por rol en el backend. La hace falta el criterio 5 de US-01 y hoy
  no la usa ningún endpoint.

Ya está hecho y no se rehace: el modelo de local y de personal, el aislamiento entre locales
con su prueba automatizada, la base de datos con su primera migración, **la API armada**
(autenticación, OpenAPI, migraciones y semilla de desarrollo) y **el ingreso de punta a punta
del lado del backend** — caso de uso, emisión del token y endpoint. Todo eso viene en el PR
`feat/autenticacion`. Lo que le falta a US-01 es la pantalla y la autorización por rol.

## Calendario

| Cuándo | Qué |
|---|---|
| **Lun 7 – Mié 9** | Los tres sobre US-01 y US-02. Es el cuello de botella: hasta que no se pueda entrar, ninguna otra story se puede terminar. Quien no esté ahí define el aspecto visual de la app. |
| **Jue 10 – Dom 13** | Tres carriles en paralelo (abajo). |
| **Dom 13** | **Hito duro: el recorrido camina entero.** Entrar → cargar un trago → pedirlo desde el celular → verlo listo. Feo, sin estilo, pero de punta a punta. |
| **Lun 14** | Congelamiento a la noche. Nadie arranca nada nuevo después de este punto. |
| **Mar 15 – Mié 16** | Errores, prueba de punta a punta, README y ensayo de la demo. |
| **Jue 17** | Entrega. |

Si el domingo 13 el recorrido no camina, se recorta **ese día**, empezando por US-13 y US-14.
Para eso está el hito: para enterarnos con cuatro días de margen y no con uno.

### Carriles del jueves 10 al domingo 13

Uno por persona, elegidos para no pisarse en los mismos archivos.

| Carril | Stories | Puntos |
|---|---|---|
| Pedidos | Lo que hay detrás de US-11, US-12, US-13 y US-14 | 18 |
| Administración | US-03, US-04, US-05, US-06, US-07, US-08 | 16 |
| Cliente | US-09, US-10 y las pantallas de US-11 y US-12 | 13 |

Las pantallas de la barra las toma quien termine primero su carril.

## Cómo nos organizamos estos diez días

1. **Máximo dos PRs abiertos a la vez, y se revisan el mismo día.** Con tres personas y diez
   días, un PR esperando cuarenta y ocho horas hace más daño que cualquier problema técnico.
2. **Una story por PR, o menos.** Un PR de cuarenta archivos no se revisa: se aprueba de memoria.
3. **Dónde gastar el presupuesto de pruebas.** El TDD estricto se sostiene donde se corrige
   diseño y donde duele el error: las reglas del pedido y el aislamiento entre locales. Las
   pantallas de carga y listado llevan el camino feliz y la prueba de punta a punta. No es
   aflojar el Definition of Done: es ponerlo donde rinde.
4. **Las decisiones de la tabla de arriba están cerradas.** Reabrir una cuesta un día que no hay.

## Fuera del alcance, a propósito

Nada de esto lo pide la consigna del Sprint 1.

| Qué | Por qué no |
|---|---|
| Notificación al celular e instalar la aplicación | La consigna permite simular las notificaciones. |
| Impresión del ticket y lectura de QR | El bartender trabaja desde la pantalla. |
| Pagar de verdad | La consigna permite simular el pago. |
| Que el cliente se cree una cuenta | La consigna pide expresamente poder pedir sin cuenta. |
| Mesas VIP, saldo de mesa, cajero y mozo | Este sprint es sólo retiro en barra, con administrador y bartender. |
| Cancelar un pedido | No está entre los seis puntos de la consigna. |
| Buscar, filtrar y paginar los listados | Con la cantidad de datos de una demo no se nota. |
| Métricas y reportes | No están en la consigna. |
