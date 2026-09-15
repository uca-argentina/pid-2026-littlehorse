# Backlog del Sprint 1

Lo que nos comprometemos a entregar de la [consigna del Sprint 1](sprint-1.md).
**Entrega: jueves 17 de septiembre de 2026.**

## Cómo escribimos las stories

Una story describe **qué necesita una persona y para qué le sirve**, nunca cómo se construye.
El "cómo" es de quien la implementa y no se negocia en la planificación.

> **Como** <rol concreto> · **quiero** <capacidad> · **para** <valor que obtiene>

- **El rol es una persona real** (cliente, administrador, mozo), nunca "usuario".
- **El "para" tiene que aportar algo.** Si dice "para poder verlo", está de más: eso ya lo
  dijo el "quiero". El "para" es lo que se pierde si la story no se hace.

La narrativa es apenas el título de la conversación. Lo que sigue es lo que importa.

## Los criterios de aceptación

**Es la parte más importante de cada story.** La narrativa dice qué se quiere; los criterios
de aceptación dicen **cómo sabemos que está cumplido**. Son las condiciones que el sistema
tiene que satisfacer para que quien pidió la funcionalidad la dé por buena — Mike Cohn los
llama *conditions of satisfaction*, y son tan definitorios que una story sin ellos no se
puede aceptar ni discutir, sólo suponer.

Ocho reglas que nos aplicamos:

1. **Se escriben antes de empezar a construir.** Escritos después describen lo que se
   construyó, no lo que se pedía, y entonces siempre dan verde.
2. **Cada criterio se responde con sí o con no.** No existe "más o menos cumplido".
3. **Se verifican usando el sistema**, apretando botones. Si para comprobar uno hay que abrir
   la base de datos o leer un log, está mal escrito.
4. **Describen el qué, no el cómo.** Sin nombres de clases, tablas, endpoints ni librerías.
   Un criterio que nombra la solución le prohíbe a quien implementa encontrar una mejor.
5. **Nada de términos vagos.** "Rápido" no es un criterio; "en menos de dos segundos" sí.
   "Intuitivo" y "amigable" no son criterios de nada.
6. **Cubren también lo que sale mal.** Qué pasa con un dato inválido, con una lista vacía o
   con un permiso que falta es parte del alcance, y si no está escrito no se construye.
7. **No son casos de prueba.** Son el índice del plan de pruebas, no el plan: un criterio
   puede necesitar varios tests.
8. **No son el Definition of Done.** El DoD es el mismo para todas las stories —tests, lint,
   cobertura, arquitectura— y vive en [CLAUDE.md](../../CLAUDE.md). Los criterios son propios
   de cada story. Para darla por terminada tienen que cumplirse **los dos**.

### Los dos formatos que usamos

**Escenario — Dado / Cuando / Entonces.** Viene de BDD; lo propuso Daniel Terhorst-North en
2003. Es el formato por defecto: sirve siempre que haya un disparador y un resultado
observable.

> **Dado** <el contexto de partida> · **cuando** <la acción> · **entonces** <lo que se ve>

**Regla — lista de condiciones.** Para lo que vale siempre y no tiene disparador:
restricciones, límites, requisitos de diseño. Forzarlo al formato de escenario lo vuelve
ilegible, así que va como lista y se marca como tal.

Referencias: [Acceptance Criteria: Purposes, Formats and Best Practices (AltexSoft)](https://www.altexsoft.com/blog/acceptance-criteria-purposes-formats-and-best-practices/) ·
[Definition of Done y Conditions of Satisfaction (Mountain Goat Software)](https://www.mountaingoatsoftware.com/blog/clarifying-the-relationship-between-definition-of-done-and-conditions-of-sa) ·
[Given-When-Then (Product School)](https://productschool.com/blog/product-fundamentals/acceptance-criteria)

### Lista para tomar

Una story entra al sprint cuando tiene narrativa, tiene criterios de aceptación escritos y no
depende de nada sin terminar. Si le falta algo, no entra.

## Quiénes usan el sistema en este sprint

| Rol | Qué hace | Se autentica |
|---|---|---|
| **Cliente** | Pide tragos desde su celular | No. No se registra ni instala nada |
| **Administrador** | Carga la carta y al personal de su local | Sí |
| **KDS** | La cuenta de la estación de barra. Se le puede crear la cuenta; sus pantallas quedaron fuera de este sprint | Sí |
| **Mozo** | Se le puede crear la cuenta, pero todavía no tiene pantalla propia | Sí |

## Resumen

| ID | Story | Depende de | Estado |
|---|---|---|---|
| US-01 | Iniciar sesión | — | ✅ **Terminada** |
| US-02 | Poder entrar la primera vez | — | ✅ **Terminada** |
| US-03 | Dar de alta al equipo | US-01 | ✅ **Terminada** |
| US-04 | Ver y corregir al equipo | US-03 | ✅ **Terminada** |
| US-05 | Dar de baja a quien se fue | US-03 | **3 de 4** — falta el criterio de los pedidos, que no existen todavía |
| US-06 | Cargar un trago en la carta | US-01 | ✅ **Terminada** |
| US-07 | Marcar que un trago se acabó | US-06 | Pendiente |
| US-08 | Corregir y sacar tragos | US-06 | Pendiente |
| US-09 | Ver la carta desde el celular | US-06 | ✅ **Terminada** |
| US-10 | Armar el pedido | US-09 | **4 de 5** — falta la aclaración por trago |
| US-11 | Confirmar el pedido | US-10 | Pendiente |
| US-12 | Seguir mi pedido | US-11 | Pendiente |
| US-13 | Encontrar a alguien en el listado | US-03 | ✅ **Terminada** |
| US-14 | Agrupar la carta por categoría | US-06, US-09 | Pendiente — planificada el 2026-09-15 |

Las doce primeras son imprescindibles: cubren los seis puntos de la consigna y nada más.
US-13 es la excepción, y entró por pedido del equipo el 2026-09-14 después de estar
recortada: el detalle está en su ficha. Las
pantallas de la barra quedaron fuera del sprint el 2026-09-10; el motivo y la consecuencia
están en *Fuera del alcance*.

**Estado al sábado 12 de septiembre.** Tres stories terminadas y nueve sin empezar. US-03
cerró de punta a punta y de paso cerró US-01: ahora un administrador entra y ve una pantalla
de gestión de verdad, que era el único criterio que le faltaba. Con eso quedó hecha también
la autorización por rol del backend, que era la pieza sin tarjeta que trababa todo el carril
de administración. De las nueve que quedan, US-04 y US-05 enchufan en el listado que ya
existe. El hito del lunes 14 sigue siendo el punto donde se decide qué entra.

**Estado al lunes 14 de septiembre.** Cinco terminadas, una a un criterio de estarlo y seis
sin empezar. Con US-04 y US-05 el ABM de personal queda completo: se da de alta, se corrige,
se da de baja y se reactiva, y nada se borra. Lo que queda es todo la carta y el pedido, que
es el camino crítico hacia el hito de hoy.

**Estado al martes 15 de septiembre.** US-06 cerró de punta a punta: un administrador entra
directo al listado de productos, carga uno con su foto, y la foto queda en Blob Storage
(Azurite en desarrollo). Con ella entraron tres decisiones que no estaban escritas —la
entidad se llama `Product` porque la carta también vende botellas, el stock es numérico y
en cero el producto queda agotado solo, y la foto se sube como archivo en vez de pegar un
enlace— y la cabecera de administración con pestañas, que reemplazó a la pantalla de inicio
de `/staff`. Quedan US-07 y US-08 en el carril de la carta, y todo el carril del cliente.

---

## Arranque

### US-01 · Iniciar sesión

> **Como** parte del personal de un local
> **quiero** entrar con mi usuario y contraseña
> **para** trabajar sobre los datos de mi local sin que nadie pueda operar en mi nombre.

**Depende de:** nada.

**Criterios de aceptación**

1. **Dado** que soy un administrador activo, **cuando** ingreso correctamente, **entonces**
   entro y veo las pantallas de gestión.
2. **Dado** que entro con una cuenta de KDS o de mozo, **cuando** ingreso correctamente,
   **entonces** el sistema me dice que todavía no hay pantallas para mi rol, en vez de
   dejarme en una pantalla vacía.
3. **Dado** que escribo mal el usuario, **y dado** que otra vez escribo mal la contraseña,
   **entonces** en los dos casos recibo exactamente el mismo mensaje, que no revela cuál de
   los dos estaba mal.
4. **Dado** que me dieron de baja, **cuando** ingreso mi contraseña correcta, **entonces** no
   me deja entrar.
5. **Dado** que entré en un local, **cuando** cambio la dirección del navegador apuntando a
   otro local, **entonces** no veo ni un dato de ese otro local.
6. **Dado** que mi sesión venció mientras trabajaba, **cuando** intento seguir usando el
   sistema, **entonces** me lleva de nuevo a la pantalla de ingreso con un mensaje distinto
   al de credenciales incorrectas.

> **Nota:** los criterios 3 y 4 son deliberados, no una omisión de usabilidad. Un mensaje que
> distinga "ese usuario no existe" de "la contraseña está mal" le permite a cualquiera
> averiguar quién trabaja en el local probando nombres.
>
> El criterio 6 sale del [ADR-0008](../adr/0008-autenticacion-con-token-unico-sin-refresh-token.md):
> la sesión dura ocho horas y vencer es un evento normal de fin de turno, no un error.

✅ **Terminada.** Los seis criterios están cumplidos y con test. El criterio 1 cerró con
US-03: un administrador que entra ve el acceso a *Usuarios internos* en la pantalla de
inicio, y los demás roles siguen viendo el aviso de que su rol todavía no tiene pantallas.

> Quedó anotado que este criterio ataba US-01 a una story posterior, y efectivamente cerró
> recién con ella. No hizo falta reescribirlo: US-03 entró antes de la fecha. Si el mismo
> patrón vuelve a aparecer, conviene que el criterio hable de lo que la propia story
> entrega.

### US-02 · Poder entrar la primera vez

> **Como** administrador de un local nuevo
> **quiero** que mi cuenta ya exista cuando el sistema se pone en marcha
> **para** no quedar afuera de un sistema donde las cuentas sólo las crea un administrador.

**Depende de:** nada.

**Criterios de aceptación**

1. **Dado** un sistema recién puesto en marcha sobre una base vacía, **cuando** abro la
   pantalla de ingreso, **entonces** existe un local con un administrador con el que puedo
   entrar.
2. **Dado** que el sistema ya se puso en marcha antes, **cuando** se reinicia, **entonces**
   no se crea un segundo administrador ni un segundo local.
3. **Dado** que nadie definió la contraseña de ese administrador, **cuando** el sistema
   arranca, **entonces** falla y avisa, en vez de crear una cuenta con una contraseña
   adivinable.

✅ **Terminada.** Los tres criterios están cubiertos por `DevelopmentSeederTests`.

---

## Personal del local

### US-03 · Dar de alta al equipo

> **Como** administrador
> **quiero** dar de alta a quienes trabajan conmigo indicando su rol
> **para** que cada uno acceda solamente a la parte del sistema que necesita.

**Depende de:** US-01.

**Criterios de aceptación**

1. **Dado** que completo usuario, contraseña y rol, **cuando** guardo, **entonces** la
   persona aparece en el listado y puede iniciar sesión enseguida.
2. **Dado** que elijo el rol, **cuando** abro las opciones, **entonces** puedo elegir entre
   administrador, KDS y mozo.
3. **Dado** que ese nombre de usuario ya existe en mi local, **cuando** intento guardar,
   **entonces** me avisa y no se crea un duplicado.
4. **Dado** que otro boliche tiene un usuario con ese mismo nombre, **cuando** lo cargo en el
   mío, **entonces** se crea sin problema.
5. **Dado** que dejo un campo vacío o escribo un usuario de menos de tres caracteres,
   **cuando** intento guardar, **entonces** el formulario me lo señala y no guarda nada.
6. **Dado** que entré con cualquier rol que no sea administrador, **cuando** escribo a mano
   la dirección de una pantalla de administración, **entonces** el sistema no me la muestra
   ni me deja operar sobre ningún recurso de administración.

✅ **Terminada.** Los seis criterios están cumplidos. El alta corre contra la API real, el
usuario nuevo aparece en el listado y entra enseguida, el nombre repetido se rechaza dentro
del boliche y se acepta en otro, y el criterio 6 está cubierto por los dos lados: el
guardián de rol en el front y la política de administrador en el backend, con una prueba de
punta a punta que le pide el listado a la API con un token de KDS y recibe 403.

> **Nota sobre los roles.** No existe un rol "bartender". El KDS es la cuenta de la
> **estación de barra**, compartida por todos los que preparan ahí, tal como lo describe §11
> del [diseño funcional](../drink.it.v2.md): la pantalla es por puesto de trabajo y no por
> persona, que es lo que mantiene baja la inversión en tablets. Los tres roles del sistema
> son entonces administrador, KDS y mozo.
>
> **Nota:** el criterio 6 vivía en US-01 y se movió acá. Allá no se podía verificar, porque
> hasta esta story no existe ninguna pantalla de administración a la que alguien pueda
> intentar entrar. Es la primera story que exige autorización por rol.
>
> Está escrito por exclusión —"cualquier rol que no sea administrador"— y no rol por rol, a
> propósito: así sigue valiendo cuando aparezcan el cajero y los demás, sin que haya que
> reescribir el criterio ni acordarse de agregar el rol nuevo a una lista.

### US-04 · Ver y corregir al equipo

> **Como** administrador
> **quiero** ver a mi personal y poder corregir sus datos
> **para** arreglar un rol mal asignado o una contraseña olvidada sin tener que borrar y
> volver a cargar a la persona.

**Depende de:** US-03.

**Criterios de aceptación**

1. **Dado** que abro el listado, **cuando** lo miro, **entonces** veo de cada persona su
   usuario, su rol y si está activa.
2. **Dado** que a alguien hay que pasarlo de mozo a administrador, **cuando** le cambio el
   rol y guardo, **entonces** la próxima vez que entra ve las pantallas del rol nuevo.
3. **Dado** que alguien olvidó su contraseña, **cuando** le cargo una nueva, **entonces**
   puede entrar con esa y no con la anterior.
4. **Dado** que hay personal cargado en otro local, **cuando** abro mi listado, **entonces**
   no aparece.
5. **Dado** que entré con cualquier rol que no sea administrador, **cuando** escribo a mano
   la dirección del listado, **entonces** el sistema no me lo muestra.

✅ **Terminada.** El listado muestra usuario, rol y estado; desde cada fila se entra a
corregir a esa persona. El cambio de rol y el reseteo de contraseña son dos acciones
separadas, una por endpoint, así que una que falla no arrastra a la otra. Los criterios 4 y
5 ya estaban cubiertos por el filtro global y por el guardián de rol, y el criterio 5 tiene
además una prueba que le pide a la API los seis endpoints de administración con un token
que no es de administrador.

### US-05 · Dar de baja a quien se fue

> **Como** administrador
> **quiero** dar de baja a quien dejó de trabajar acá
> **para** que no pueda volver a entrar, sin perder el registro de los pedidos que preparó.

**Depende de:** US-03.

**Criterios de aceptación**

1. **Dado** que doy de baja a alguien, **cuando** intenta iniciar sesión, **entonces** no
   puede entrar.
2. **Dado** que lo di de baja, **cuando** miro el listado, **entonces** sigue estando, marcado
   como inactivo.
3. **Dado** que preparó pedidos antes de la baja, **cuando** consulto esos pedidos,
   **entonces** siguen mostrando quién los preparó.
4. **Dado** que esa persona vuelve a trabajar, **cuando** la reactivo, **entonces** entra con
   su cuenta de siempre, sin cargarla de nuevo.

**3 de 4.** Los criterios 1, 2 y 4 están cumplidos y con prueba de punta a punta: se da de
baja, la fila sigue en el listado marcada, esa persona no entra ni con la contraseña
correcta, y al reactivarla vuelve con su cuenta de siempre. Falta el criterio 3, y no por
algo de la baja: **los pedidos no existen hasta US-11**, así que no hay nada a lo que
consultarle quién lo preparó. La baja lógica ya deja el dato preparado.

> **Una regla que no estaba en los criterios.** El boliche nunca puede quedarse sin un
> administrador activo: dar de baja al último, o sacarle el rol, se rechaza. No alcanzaba
> con prohibir tocar la cuenta propia — el rol y el estado activo viajan en un token de ocho
> horas que no se vuelve a chequear, así que dos administradores podían darse de baja
> mutuamente y dejar el local sin nadie que pudiera administrarlo. Se cuenta cuántos
> quedarían, que es lo que corta el caso sin importar quién pregunta.

> **Limitación conocida, decidida a propósito.** Dar de baja **no corta la sesión que ya está
> abierta**: si esa persona estaba trabajando, sigue pudiendo hasta que le venza el token, y
> eso puede tardar hasta ocho horas. Está aceptado en el
> [ADR-0008](../adr/0008-autenticacion-con-token-unico-sin-refresh-token.md), que también
> explica cuándo hay que volver sobre esa decisión. Por eso el "para" de esta story dice
> "que no pueda volver a entrar" y no "cerrarle el acceso": la story promete exactamente lo
> que el sistema hace.

### US-13 · Encontrar a alguien en el listado

> **Como** administrador
> **quiero** buscar por nombre de usuario y filtrar por rol
> **para** llegar a una persona sin leer la lista entera.

**Depende de:** US-03.

**Criterios de aceptación**

1. **Dado** que escribo parte de un nombre de usuario, **cuando** miro el listado, **entonces**
   sólo quedan quienes lo contienen, sin importar cómo lo escribí en mayúsculas.
2. **Dado** que elijo un rol, **cuando** miro el listado, **entonces** sólo quedan los de ese
   rol, incluidos los que están dados de baja.
3. **Dado** que uso la búsqueda y el rol a la vez, **cuando** miro el listado, **entonces** se
   aplican los dos.
4. **Dado** que cada rol muestra cuántos tiene, **cuando** busco algo, **entonces** ese número
   cuenta lo que quedó de la búsqueda y no el total del boliche.
5. **Dado** que no coincide nadie, **cuando** miro el listado, **entonces** me lo dice con un
   mensaje distinto al de un boliche sin gente cargada.

> **Estaba recortada a propósito y volvió.** Hasta el 2026-09-14 figuraba en *Fuera del
> alcance* con el motivo "con la cantidad de datos de una demo no se nota". El equipo pidió
> incorporarla igual. Entra como story propia y no dentro de US-04, para no reescribir
> criterios ya acordados después de haberlos construido.
>
> **Se filtra en el navegador, no en la API.** El equipo entero de un boliche entra en una
> sola respuesta, así que no hay endpoint nuevo, ni paginado, ni una consulta por tecla
> apretada. Cuando un boliche tenga cientos de usuarios habrá que mover esto al servidor, y
> ahí aparecen el índice, el orden estable y el cursor.

✅ **Terminada.** Los cinco criterios están cubiertos por `StaffUsersPage`.

---

## La carta

### US-06 · Cargar un trago en la carta

> **Como** administrador
> **quiero** cargar un trago con nombre, descripción, foto, stock y precio
> **para** que el cliente elija sabiendo qué es y cuánto sale, sin preguntarle a nadie.

**Depende de:** US-01.

**Criterios de aceptación**

1. **Dado** que completo nombre, descripción, foto y precio, **cuando** guardo, **entonces**
   el trago aparece en la carta que ve el cliente.
2. **Dado** que dejo el precio en cero o en negativo, **cuando** intento guardar,
   **entonces** no me deja y me dice por qué.
3. **Dado** que dejo el nombre vacío, **cuando** intento guardar, **entonces** el formulario
   me lo señala y no guarda nada.
4. **Dado** que cargué una foto, **cuando** el cliente abre la carta, **entonces** la ve junto
   al trago.
5. **Dado** que la foto que cargué no se puede mostrar, **cuando** el cliente abre la carta,
   **entonces** ve el trago igual, con un espacio de imagen vacío, y nunca una pantalla rota.

✅ **Terminada.** El alta corre contra la API real: nombre, descripción, precio, stock y foto,
y el producto aparece en el listado de administración con su foto servida desde el storage.
Los criterios 2 y 3 se cumplen en el formulario antes de mandar nada y en la API después
(precio en cero → 400 con su tipo de problema; nombre vacío → el formulario lo señala). El
criterio 5 está cubierto con el mecanismo que va a usar la carta del cliente: un producto sin
foto, o cuya foto no carga, muestra el ícono de `public/images` en su lugar.

> **La mitad de los criterios 1, 4 y 5 que dice "la carta que ve el cliente" se verifica en
> US-09**, que es donde esa pantalla existe. Mismo patrón que se anotó en US-01: el criterio
> ata la story a una posterior. Lo que US-06 entrega y prueba es que el producto está en la
> carta del boliche, con su foto, listo para que US-09 lo muestre.
>
> **Decisiones que entraron con la story (2026-09-14/15).** La entidad se llama `Product` y
> no `Drink`: la carta también vende botellas. El **stock es numérico** y obligatorio; en
> cero el producto queda agotado solo, y aparte existe el interruptor de disponibilidad que
> construye US-07. El **nombre es único por boliche**, como el usuario de US-03. La **foto se
> sube como archivo** —JPEG, PNG o WebP, hasta 5 MB, sin redimensionar— a Azure Blob
> Storage, con Azurite en desarrollo; la tabla de decisiones de abajo quedó corregida.

### US-07 · Marcar que un trago se acabó

> **Como** administrador
> **quiero** marcar un trago como agotado
> **para** que nadie lo pida y lo pague cuando no se lo podemos preparar.

**Depende de:** US-06.

**Criterios de aceptación**

1. **Dado** que marco un trago como agotado, **cuando** el cliente abre la carta,
   **entonces** lo ve indicado como agotado y no lo puede agregar al pedido.
2. **Dado** que repusimos, **cuando** lo vuelvo a habilitar, **entonces** el cliente puede
   pedirlo otra vez, sin que yo haya tenido que cargarlo de nuevo.
3. **Dado** que un cliente ya lo tenía en su pedido sin confirmar, **cuando** lo marco
   agotado, **entonces** al confirmar el cliente se entera y no se le cobra algo que no
   vamos a preparar.

> **Nota:** el trago agotado se muestra en vez de ocultarse a propósito. Si desapareciera de
> la carta, el cliente pensaría que se cargó mal e iría a preguntar a la barra, que es
> exactamente la caminata que el producto quiere evitar. El diseño ya lo resuelve: la tarjeta
> aparece atenuada y sin el botón de agregar.

### US-08 · Corregir y sacar tragos

> **Como** administrador
> **quiero** editar un trago o sacarlo de la carta
> **para** mantenerla fiel a lo que vendemos hoy, sin que se me modifiquen los pedidos que ya
> se cobraron.

**Depende de:** US-06.

**Criterios de aceptación**

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

**Depende de:** US-06.

**Criterios de aceptación**

1. **Dado** que escaneo el QR del boliche, **cuando** se abre la página, **entonces** veo la
   carta de ese boliche.
2. **Dado** que entro por primera vez, **cuando** navego la carta, **entonces** en ningún
   momento me pide crear una cuenta, iniciar sesión ni descargar una aplicación.
3. **Dado** que la carta está cargando, **cuando** miro la pantalla, **entonces** veo que
   está cargando, y no una pantalla en blanco.
4. **Dado** que no hay conexión o el sistema falla, **cuando** intento ver la carta,
   **entonces** me lo dice y me ofrece reintentar.
5. **Dado** que el boliche todavía no cargó ningún trago, **cuando** abro la carta,
   **entonces** me lo dice, en vez de mostrarme una lista vacía sin explicación.

**Reglas**

- Se usa con una mano: todo lo que hay que tocar queda al alcance del pulgar.
- Se lee de noche, con poca luz y a los tirones: el diseño oscuro aprobado, sin texto por
  debajo de 13 píxeles.
- Los tragos agotados se distinguen de los disponibles a simple vista.

**Decisiones tomadas el 2026-09-15, antes de construirla.**

| Tema | Cómo queda |
|---|---|
| Dirección del QR | `/{venueSlug}/menu`. Deja `/{venueSlug}` libre por si más adelante hace falta una bienvenida antes de la carta. |
| Encabezado | El nombre real del boliche, que la respuesta de la carta devuelve junto con los tragos. Hasta ahora la app sólo conocía el slug. |
| Buscador | **Entra, y no era un criterio.** Lo pidió el equipo hoy. Es un elemento más de una pantalla que se construye de cero y no toca nada ya cerrado, así que va acá adentro en vez de en una ficha aparte. Filtra en el navegador sobre la carta ya cargada, sin endpoint nuevo. |
| Precio | `$ 4.500,00`, formato argentino con los dos decimales siempre. |
| Categorías | **No entran acá.** Están en el wireframe y no en los criterios, y la categoría vive en `Product`, no en la carta. Es US-14. |

✅ **Terminada.** Los cinco criterios están cumplidos y con prueba de punta a punta que abre
la dirección del QR sin sesión. Es la primera pantalla de la app que no es de personal: no
hay guardián de sesión, no se crea nada en el navegador, y hay un test que lo verifica
leyendo el almacenamiento después de mirar la carta.

> **Lo que la carta no cuenta.** La respuesta del cliente no lleva el stock ni el motivo por
> el que algo no se puede pedir. Un trago agotado y uno que el boliche apagó esta noche se
> ven iguales desde el teléfono, que es todo lo que necesita saber quien pide. Hay un test
> que revisa que la palabra *stock* no aparezca en la respuesta.
>
> **Un slug que no existe da 404**, distinto de un boliche que existe y todavía no cargó
> nada, que da lista vacía. En el teléfono son dos pantallas completamente distintas y el
> criterio 5 depende de esa diferencia.

### US-14 · Agrupar la carta por categoría

> **Como** cliente
> **quiero** que la carta esté separada en tragos, cervezas y sin alcohol
> **para** encontrar lo que busco sin recorrer la lista entera.

**Depende de:** US-06 y US-09.

**Criterios de aceptación**

1. **Dado** que cargo un trago, **cuando** completo el formulario, **entonces** tengo que
   elegir una categoría entre tragos, cervezas y sin alcohol.
2. **Dado** que abro la carta, **cuando** miro arriba, **entonces** tengo una solapa por
   categoría más una de *Todos*, y la carta abre en *Todos*.
3. **Dado** que elijo una solapa, **cuando** miro la carta, **entonces** sólo quedan los de
   esa categoría, agotados incluidos.
4. **Dado** que había tragos cargados antes de que existieran las categorías, **cuando**
   abro la carta, **entonces** aparecen en *Tragos*.

> **Por qué ficha propia.** Se pidió el 2026-09-15, mientras se planificaba US-09. No es un
> criterio de US-09 y tampoco entra reabriendo US-06: la categoría es una columna nueva en
> `Product`, con su migración y un campo más en el alta, o sea trabajo sobre una story ya
> construida y cerrada. Mismo tratamiento que US-13.
>
> **Decidido:** lista fija en el código, como el enum de roles, sin tabla de categorías ni
> texto libre. Obligatoria al cargar, y la migración le pone *Tragos* a lo que ya esté
> cargado. Las solapas del cliente van sin conteo, a diferencia de las de administración,
> porque así lo dibuja el wireframe del cliente.
>
> **Se construye después de US-09**, no antes: la carta tiene que estar caminando primero.

### US-10 · Armar el pedido

> **Como** cliente
> **quiero** elegir tragos con su cantidad y dejar una aclaración en cada uno
> **para** recibir exactamente lo que quiero sin tener que gritarlo en la barra por encima
> de la música.

**Depende de:** US-09.

**Criterios de aceptación**

1. **Dado** que elijo un trago, **cuando** lo agrego, **entonces** aparece en mi pedido con el
   total actualizado.
2. **Dado** que ya lo agregué, **cuando** cambio la cantidad o lo saco, **entonces** el total
   se ajusta al instante.
3. **Dado** que quiero pedirlo de una forma particular, **cuando** escribo "sin hielo" en ese
   trago, **entonces** la aclaración queda guardada en ese trago y no en todo el pedido.
4. **Dado** que me llamó alguien por teléfono y salí de la app, **cuando** vuelvo,
   **entonces** mi pedido sigue armado como lo dejé.
5. **Dado** que mi pedido está vacío, **cuando** miro la pantalla, **entonces** no puedo
   avanzar a confirmar.

**4 de 5, todo desde la carta.** Se construyó el `+` de cada tarjeta, el `−` con la cantidad
en los tragos que ya están en el pedido, y la barra de abajo con el resumen y el total. Con
eso quedan cumplidos los criterios 1, 2, 4 y 5. Falta el 3, la aclaración por trago, que
necesita la pantalla del pedido.

| Decisión | Cómo queda |
|---|---|
| Dónde vive el pedido a medio armar | En el navegador, con una clave por boliche. Sobrevive a que atiendan un llamado o cierren la pestaña, que es el criterio 4, y el pedido de un local nunca aparece en otro. Cuando exista el pedido de verdad, en US-11, se muda al servidor. |
| Qué hace la barra de abajo | Nada, todavía. Tiene el aspecto exacto del wireframe, dorada y con el total, pero dice "Tu pedido" y no "Ver pedido": la pantalla del pedido no existe, y el dorado invita a tocar. Cambia una palabra cuando esa pantalla llegue. |
| Cambiar cantidades | Desde la tarjeta de la carta, con el `−`, la cantidad y el `+`. Aparecen recién cuando el trago está en el pedido, y sacar el último lo saca del pedido en vez de dejar una línea en cero. |
| El botón de usuario del encabezado | **No entra.** Llevaría a un login de cliente, y las cuentas de cliente están fuera del Sprint 1 porque la consigna pide expresamente poder pedir sin cuenta. Además el criterio 2 de US-09 está construido y con prueba de que la carta no ofrece iniciar sesión. |
| Tocar dos veces el mismo trago | Sube la cantidad de esa línea, no abre una segunda. |

### US-11 · Confirmar el pedido

> **Como** cliente
> **quiero** confirmar mi pedido dejando mi nombre
> **para** que empiecen a prepararlo y tener un número con el cual reclamarlo en la barra.

**Depende de:** US-10.

**Criterios de aceptación**

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
6. **Dado** que toco confirmar dos veces porque la señal está lenta, **cuando** miro mis
   pedidos, **entonces** se generó uno solo.

> **Nota:** el pago está simulado en este sprint, como habilita la consigna. Por eso confirmar
> equivale a pagar.

### US-12 · Seguir mi pedido

> **Como** cliente
> **quiero** ver en qué estado está mi pedido
> **para** acercarme a la barra recién cuando está listo, en vez de esperar parado.

**Depende de:** US-11.

**Criterios de aceptación**

1. **Dado** que confirmé mi pedido, **cuando** miro la pantalla de seguimiento, **entonces**
   veo mi número de pedido y el estado en el que está.
2. **Dado** que la pantalla muestra el recorrido completo —esperando, en preparación, listo,
   entregado—, **cuando** la miro, **entonces** distingo en cuál de esos pasos está el mío.
3. **Dado** que el estado de mi pedido cambia, **cuando** tengo la pantalla abierta,
   **entonces** se actualiza sola, sin que yo refresque, en menos de cinco segundos.
4. **Dado** que cerré la página, **cuando** vuelvo al mismo enlace más tarde, **entonces**
   sigo viendo mi pedido y su estado actual.
5. **Dado** que alguien conoce mi número de pedido, **cuando** intenta llegar a mi pantalla de
   seguimiento probando números en la dirección, **entonces** no lo consigue.
6. **Dado** que me quedé sin señal un momento, **cuando** vuelve la conexión, **entonces** la
   pantalla se pone al día sola, sin que yo haga nada.

> **Nota:** "se actualiza sola" se resuelve **consultando cada tres segundos**, no con
> SignalR. SignalR está en el stack y llega más adelante; construirlo ahora se lleva un día
> que no tenemos y el criterio se cumple igual.
>
> **Sin las pantallas de la barra, en este sprint el pedido no pasa de "esperando".** El
> criterio 3 se verifica cambiando el estado a mano en la base y viendo que la pantalla se
> pone al día sola. Es una limitación de la demo, no del diseño: la pantalla ya muestra los
> cuatro pasos y el mecanismo de actualización queda construido y probado.

---

## Decisiones que tomamos para llegar a la fecha

En cada caso se eligió la opción más barata que cumple lo que se pide. Ninguna cierra la
puerta a la versión definitiva.

| Tema | Cómo queda en este sprint | Cómo va a ser después |
|---|---|---|
| Foto del trago | Sube el archivo desde el formulario; va a Azure Blob Storage, con Azurite en desarrollo. Sin redimensionar | Miniaturas para la carta si el peso de las fotos se nota en el celular |
| Aviso de "listo" | La pantalla del cliente consulta cada tres segundos | Notificación al celular, y SignalR en vez de consultar |
| Pago | Confirmar el pedido equivale a pagarlo | Pago digital, efectivo en caja y saldo de mesa VIP |
| Ticket de la barra | Se trabaja desde la pantalla del KDS | Se imprime el ticket y se escanea el QR |

## Trabajo que no es una story

Nadie lo puede "ver funcionando", así que no lleva tarjeta propia: va adentro de la primera
story que lo necesita.

**Sigue pendiente:**

- **Autorización por rol en el backend.** La necesita el criterio 6 de US-03. Hoy el único
  endpoint que existe es el de ingreso, y ninguno usa `RequireAuthorization`.
- **Los de punta a punta en la CI.** Playwright ya está instalado y cubre el ingreso, pero la
  suite se corre a mano: ese job necesita SQL Server y el backend en el runner, y es una
  decisión aparte.
- **El prefijo `/api` fuera de desarrollo.** Hoy funciona por el proxy del servidor de
  desarrollo (`frontend/proxy.conf.json`, que reenvía `/api` a la API y le saca el prefijo).
  Para desplegar desde `main` lo tiene que hacer Static Web Apps en su
  `staticwebapp.config.json`, y todavía no existe ni `infra/` ni ese archivo.
- **La cuenta de Storage para las fotos.** US-06 guarda las fotos en Blob Storage y en
  desarrollo usa Azurite. En Azure hace falta una cuenta Standard LRS con lectura pública de
  blobs, y su cadena de conexión en `ImageStorage__ConnectionString`. Es el único recurso
  fuera de los tiers gratuitos; el detalle está en la adenda del
  [ADR-0007](../adr/0007-hosting-en-azure-a-costo-cero.md). Va con `infra/`.

**Ya está hecho y no se rehace.** Del backend: el modelo de local y de personal, el
aislamiento entre locales con su prueba, la base con su migración, la API armada
(autenticación, OpenAPI, migraciones y semilla de desarrollo) y los errores como problem
details.

Del frontend, que hasta el miércoles no tenía ni una ruta: el sistema de diseño con los
tokens de los wireframes, las tipografías servidas desde nuestro propio dominio, el ruteo
lazy, el guardián de sesión, el interceptor que adjunta y descarta el token, y los tipos
generados del OpenAPI. Las diez stories que quedan enchufan ahí en vez de empezar de cero.

Y están terminados **los trece diseños de pantalla**, en alta fidelidad y con un único
sistema visual: no queda ninguna decisión de diseño por tomar.

Con US-03 se sumó la **autorización por rol**, que era trabajo sin tarjeta: la política de
administrador en el backend, el guardián de rol en el front y el 403 con su propio tipo de
problema para que la PWA no confunda "no es tu rol" con "se venció tu sesión".

Con US-06 se sumaron la **cabecera de administración** con pestañas, chip de cuenta y menú
para celular —la pantalla de inicio de `/staff` quedó sólo para los roles que todavía no
tienen pantallas—, el **puerto `IImageStore`** con su implementación en Blob Storage, y la
detección del formato de imagen por los bytes del archivo, que US-08 reutiliza tal cual para
cambiar la foto.

## Calendario

| Cuándo | Qué |
|---|---|
| **Jue 10 – Vie 11** | Cerrar US-01: sistema de diseño, andamiaje de Angular y pantalla de ingreso. Más la autorización por rol, que destraba US-03. |
| **Sáb 12 – Lun 14** | Tres carriles en paralelo (abajo). |
| **Lun 14** | **Hito duro: el recorrido camina entero.** Entrar → cargar un trago → pedirlo desde el celular → seguirlo. Congelamiento a la noche: nadie arranca nada nuevo. |
| **Mar 15 – Mié 16** | Errores, prueba de punta a punta, README y ensayo de la demo. |
| **Jue 17** | Entrega. |

### Carriles del sábado 12 al lunes 14

Uno por persona, elegidos para no pisarse en los mismos archivos.

| Carril | Stories |
|---|---|
| Pedidos | Lo que hay detrás de US-11 y US-12 |
| Administración | US-03, US-04, US-05, US-06, US-07, US-08 |
| Cliente | US-09, US-10 y las pantallas de US-11 y US-12 |

Quien termine primero su carril ayuda en el de pedidos, que es el más pesado.

## Cómo nos organizamos lo que queda

1. **Máximo dos PRs abiertos a la vez, y se revisan el mismo día.** Un PR esperando cuarenta
   y ocho horas hace más daño que cualquier problema técnico.
2. **Una story por PR, o menos.** Un PR de cuarenta archivos no se revisa: se aprueba de
   memoria.
3. **Dónde gastar el presupuesto de pruebas.** El estándar que se le aplicó a la
   autenticación —dos ADR, un pipeline de errores con once tests— es el correcto para
   autenticación y no entra siete veces más en una semana. De acá en adelante: ningún ADR
   nuevo, y la profundidad de pruebas reservada a las reglas del pedido, que es lo único que
   queda con lógica de verdad. Las pantallas de carga y listado llevan el camino feliz y la
   prueba de punta a punta.
4. **Las decisiones de la tabla de arriba están cerradas.** Reabrir una cuesta un día que no
   hay.

## Fuera del alcance, a propósito

Nada de esto lo pide la consigna del Sprint 1.

| Qué | Por qué no |
|---|---|
| Notificación al celular e instalar la aplicación | La consigna permite simular las notificaciones. |
| **Las pantallas de la barra (el KDS)** | Recortadas el 2026-09-10 para llegar a la fecha. No están entre los seis puntos de la consigna. Consecuencia asumida: el rol KDS se puede crear pero no tiene pantalla, y el pedido del cliente no avanza más allá de "esperando". |
| Impresión del ticket y lectura de QR | Llegan con las pantallas de la barra. |
| Pagar de verdad | La consigna permite simular el pago. |
| Que el cliente se cree una cuenta | La consigna pide expresamente poder pedir sin cuenta. |
| Mesas VIP, saldo de mesa y cajero | Este sprint es sólo retiro en barra. |
| Las pantallas del mozo | El rol se puede elegir al dar de alta a alguien, pero la entrega en mesa llega con el sector VIP. |
| Cancelar un pedido | No está entre los seis puntos de la consigna. |
| Paginar los listados | Un boliche no tiene tanta gente ni tantos tragos como para necesitarlo. La búsqueda sí se incorporó: es US-13. |
| Límite de intentos de ingreso | Pendiente reconocido en el ADR-0008; se resuelve con lo que ya trae el framework. |
| Métricas y reportes | No están en la consigna. |
