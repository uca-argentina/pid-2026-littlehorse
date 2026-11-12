# Comandos útiles — drink.it

Todos se corren desde la **raíz del repo** (`drink-it/`), salvo que se diga otra cosa.
Para cada comando: **para qué sirve**, **cuándo usarlo** y **qué tenés que ver**.

---

## 0. El orden típico durante el examen

| Momento | Comando | Sección |
|---|---|---|
| Al empezar | `docker compose up -d` | 1 |
| Al empezar | `git checkout -b feat/...` | 9 |
| Mientras escribís el backend (cada tanto) | `dotnet build backend/src/DrinkIt.Api` | 2 |
| Cuando terminaste las propiedades nuevas | `dotnet ef migrations add ...` | 3 |
| Cuando terminaste el endpoint | `dotnet run --project backend/src/DrinkIt.Api` + Scalar | 2 |
| Para comprobar que se guardó | `sqlcmd` (consulta) | 4 |
| Antes de tocar el front | `generate:api` + `prettier` sobre `schema.d.ts` | 5 |
| Mientras escribís el front | `pnpm --prefix frontend run build` | 5 |
| Para probar en el navegador | `pnpm --prefix frontend start` | 5 |
| Al final | el checklist de la sección 12 | 12 |

---

## 1. La base de datos (Docker)

La base (SQL Server) y el emulador de fotos (Azurite) corren en **contenedores de Docker**.
**Docker Desktop tiene que estar abierto** antes de cualquiera de estos.

### `docker compose up -d`
- **Para qué:** prende SQL Server (`localhost,1433`) y Azurite (`:10000`). `-d` = en segundo plano,
  te devuelve la terminal.
- **Cuándo:** **lo primero de todo**, una vez por sesión. **La API no arranca sin la base.**
- **Qué ver:** `Container drinkit-sql Started` (o `Running` si ya estaba).

### `docker compose ps`
- **Para qué:** ver si los contenedores están corriendo.
- **Cuándo:** la API dice que no puede conectarse a la base, o no te acordás si la prendiste.
- **Qué ver:** `drinkit-sql` con estado `Up ... (healthy)`.

### `docker compose down`
- **Para qué:** apagar los contenedores. **Los datos no se borran** (quedan en un volumen).
- **Cuándo:** al terminar, para liberar memoria.

### `docker compose logs sql` · `docker compose restart sql`
- **Para qué:** ver el log de SQL Server / reiniciarlo.
- **Cuándo:** la base no responde aunque `ps` diga que está prendida.

---

## 2. Backend (.NET)

### `dotnet build backend/src/DrinkIt.Api`
- **Para qué:** **compilar** el código de producción (Api + Application + Infrastructure + Domain),
  **sin** los tests. Es rápido.
- **Cuándo:** **cada vez que terminás un paso** (el método del dominio, el handler, el endpoint).
  Es la forma de saber si lo que escribiste es C# válido.
- **Qué ver:** `Compilación correcta. 0 Errores`. Si hay errores, ver la sección 11 para leerlos.

### `dotnet build backend/DrinkIt.slnx`
- **Para qué:** compilar **todo, tests incluidos**.
- **Cuándo:** al terminar el backend, para comprobar que **no rompiste tests viejos** (pasa si
  agregaste un parámetro sin default a un record o un método a una interfaz).
- **Qué ver:** `0 Errores`.

### `dotnet run --project backend/src/DrinkIt.Api`
- **Para qué:** **levantar la API**. En Development, al arrancar **aplica las migraciones pendientes** y
  siembra los datos iniciales (boliche, admin, categorías).
- **Cuándo:** para probar un endpoint (Scalar, curl o el front), después de una migración nueva, o
  antes de `generate:api`. **Queda corriendo**: usá otra terminal para lo demás. **Ctrl+C** la corta.
  Si cambiás código del backend, cortala y volvé a levantarla.
- **Qué ver:** al final `Now listening on: https://localhost:7176` y `Application started`. Más arriba,
  `Applying migration '...'` si había migraciones nuevas, con el SQL que ejecutó (`ALTER TABLE ...`).
- **Si se cae:** buscá la primera línea con `fail:` o `Unhandled exception`.

### Scalar — `https://localhost:7176/scalar` (en el navegador, no es un comando)
- **Para qué:** ver **todos los endpoints** y **ejecutarlos** sin escribir front ni curl.
- **Cuándo:** apenas terminás un endpoint, **antes** de tocar el front.
- **Qué ver:** que el body pida los campos que esperás, y los status (404 con datos falsos, 204/200 con
  datos reales).
- Si el navegador avisa que el certificado no es seguro, aceptalo: es el de desarrollo.

### `dotnet test backend/tests/DrinkIt.ArchitectureTests`
- **Para qué:** correr los tests que verifican **las reglas de arquitectura**: Domain sin dependencias,
  sin setters públicos, toda entidad con `VenueId` tiene filtro global, auditoría...
- **Cuándo:** **siempre al final** del backend, y sí o sí si creaste una **entidad nueva**.
- **Qué ver:** `Correctas: N, Con errores: 0`.

### Otros tests

| Comando | Para qué / cuándo |
|---|---|
| `dotnet test backend/tests/DrinkIt.Domain.Tests` | Tests del dominio, rapidísimos. Si tocaste una entidad, para ver que no rompiste reglas viejas. |
| `dotnet test backend/tests/DrinkIt.Application.Tests` | Tests de los handlers (con fakes). Si tocaste un handler existente. |
| `dotnet test backend/DrinkIt.slnx` | **Todos**. Los de integración levantan su propio SQL con Docker: tarda varios minutos. Al final, si te sobra tiempo. |
| `dotnet test ... --filter "FullyQualifiedName~Rate"` | Sólo los tests cuyo nombre contiene "Rate". Para correr uno puntual. |

### `dotnet format backend/DrinkIt.slnx`
- **Para qué:** **arregla solo** el formato del código C# (espacios, líneas en blanco, `using`).
- **Cuándo:** al terminar el backend, antes de entregar.
- La variante `--verify-no-changes` **sólo verifica**, sin cambiar nada (es la que corre la CI).

---

## 3. Migraciones (EF Core)

Una **migración** es un archivo C# que dice cómo cambiar la base (agregar una columna, crear una
tabla). **No se escribe a mano: se genera.** EF compara tu configuración actual
(`XxxConfiguration.cs`) con la última "foto" (`DrinkItDbContextModelSnapshot.cs`) y escribe la diferencia.

### `dotnet tool restore`
- **Para qué:** instala la herramienta `dotnet-ef` en la versión que fija el repo
  (`.config/dotnet-tools.json`).
- **Cuándo:** **una sola vez**, antes del primer `dotnet ef`. Si `dotnet ef` dice "no se encontró el
  comando", corré esto.

### `dotnet ef migrations add <Nombre>`

```bash
dotnet ef migrations add AddOrderRating \
  --project backend/src/DrinkIt.Infrastructure \
  --startup-project backend/src/DrinkIt.Api \
  --output-dir Persistence/Migrations
```

- **Para qué:** **crear la migración** con los cambios de la base.
- **Cuándo:** cuando **agregaste o cambiaste propiedades** de una entidad, o creaste una entidad nueva,
  **y ya las mapeaste** en su `XxxConfiguration`. ⚠️ **Con los nombres definitivos**: el nombre de la
  columna sale del de la propiedad, y renombrar después obliga a otra migración.
- **Qué significa cada parte:**
  - `AddOrderRating`: el nombre, en PascalCase, que diga qué cambia.
  - `--project`: dónde viven el `DbContext` y las migraciones (Infrastructure).
  - `--startup-project`: el proyecto que arranca y tiene la configuración (Api).
  - `--output-dir`: la carpeta donde ya están las otras migraciones.
- **Qué ver:** 3 archivos: `<fecha>_AddOrderRating.cs`, `<fecha>_AddOrderRating.Designer.cs` (no se toca) y
  `DrinkItDbContextModelSnapshot.cs` actualizado. **Abrí el primero** y verificá que el `Up()` tenga
  **sólo** lo que esperás y el `Down()` lo inverso. Si aparece otra tabla, frená.
- **Cómo se aplica:** sola, la próxima vez que levantás la API.

### `dotnet ef migrations remove` (con los mismos `--project` y `--startup-project`)
- **Para qué:** **borrar la última migración** generada.
- **Cuándo:** te equivocaste (nombre de columna mal, faltó una propiedad) y **todavía no levantaste la
  API**, o sea, no se aplicó. Corregís y la generás de nuevo.

### `dotnet ef migrations list` (con los mismos `--project` y `--startup-project`)
- **Para qué:** listar las migraciones y ver cuáles están aplicadas (las pendientes dicen `(Pending)`).
- **Cuándo:** dudás de si tu migración se aplicó.

---

## 4. Ver la base de datos

**Datos de conexión:** servidor `localhost,1433` · usuario `sa` · clave `DrinkIt!Local1` · base
**`drinkit`**. Salen de `docker-compose.yml`; no es secreta, sólo protege tu contenedor local.

### Opción visual: extensión **SQL Server (mssql)** de VS Code
- **Para qué:** navegar las tablas y ver los datos con el mouse.
- **Cómo:** Cmd+Shift+X → instalar "SQL Server (mssql)" → ícono nuevo en la barra izquierda →
  Add Connection → los datos de arriba → **Trust server certificate: sí** → `drinkit` → Tables →
  clic derecho → Select Top 1000.
- ⚠️ El panel de **Docker** (Containers) **no** muestra tablas, sólo si los contenedores están prendidos.

### Opción terminal: `sqlcmd` adentro del contenedor

```bash
docker exec -it drinkit-sql /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'DrinkIt!Local1' -C -d drinkit \
  -Q "SELECT TOP 5 Code, Status, Rating FROM Orders ORDER BY CreatedAt DESC"
```

- **Para qué:** ejecutar una consulta SQL sin instalar nada.
- **Cuándo:** para **comprobar que algo se guardó**, o para **buscar datos reales** para probar un endpoint.
- **Qué significa cada parte:** `docker exec -it drinkit-sql` = "ejecutá esto adentro del contenedor de
  la base" · `-d drinkit` = la base · `-C` = aceptar el certificado · `-Q "..."` = la consulta.
- Sin `-Q` queda **interactivo**: escribís SQL, terminás con una línea `GO`, salís con `exit`.

**Los enums se guardan como número.** `OrderStatus`: 0 Cart · 1 AwaitingPayment · 2 Paid ·
3 Queued · 4 InPreparation · 5 Ready · 6 Delivered · 7 Canceled.

### Consultas útiles

```sql
-- Buscar un pedido entregado para probar un endpoint de cliente (te da slug, code y token)
SELECT TOP 5 o.Code, o.TrackingToken, v.Slug, o.Rating
FROM Orders o JOIN Venues v ON v.Id = o.VenueId
WHERE o.Status = 6 AND o.Rating IS NULL
ORDER BY o.DeliveredAt DESC;

-- ¿Se aplicó mi migración?
SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;

-- ¿Qué columnas tiene la tabla? (¿se crearon las mías?)
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Orders';
```

---

## 5. Frontend (Angular)

Se usa **pnpm**, no npm. `--prefix frontend` = "corré esto en la carpeta `frontend`" (así no hace
falta hacer `cd`).

### `pnpm --prefix frontend install`
- **Para qué:** instalar las dependencias del front (crea `node_modules`).
- **Cuándo:** la primera vez, o si `start`/`build` dicen que falta un paquete.

### `pnpm --prefix frontend run generate:api`
- **Para qué:** **regenerar `src/app/core/api/schema.d.ts`**, los tipos de TypeScript de todos los
  DTOs. Los baja del OpenAPI de la API.
- **Cuándo:** **cada vez que agregaste o cambiaste un endpoint o un record** de request/response en el
  backend, **antes** de escribir el front. ⚠️ **La API tiene que estar corriendo.**
- **Qué ver:** buscá tu DTO nuevo en `schema.d.ts` (por ejemplo `RateOrderRequest`) y fijate los campos.

### `pnpm --prefix frontend exec prettier --write src/app/core/api/schema.d.ts`
- **Para qué:** formatear `schema.d.ts` con el estilo del proyecto. El generador escribe con otro
  formato.
- **Cuándo:** **siempre, justo después de `generate:api`**.
- **Qué ver:** `git diff --stat frontend/src/app/core/api/schema.d.ts` → pocas líneas (si dice ~8000,
  no lo formateaste).

### `pnpm --prefix frontend run build`
- **Para qué:** **compilar** el front. Es la forma de ver errores de TypeScript y de plantillas.
- **Cuándo:** cada vez que terminás una pieza (service, store, página).
- **Qué ver:** `Output location: ...` = compiló. Los avisos `exceeded maximum budget` de `.scss` **ya
  existían**: ignoralos. Los `ERROR` no.

### `pnpm --prefix frontend start`
- **Para qué:** **levantar el front** en `http://localhost:4200`. Se recarga solo cuando guardás.
- **Cuándo:** para probar en el navegador. **Queda corriendo** (Ctrl+C la corta). Necesita la API
  levantada para que funcionen los datos.
- En desarrollo, el front llama a `/api/...` y `proxy.conf.json` lo reenvía a
  `https://localhost:7176` sacándole el `/api`.

### `pnpm --prefix frontend run lint`
- **Para qué:** revisar reglas de código (ESLint) y de estilos (Stylelint).
- **Cuándo:** al terminar el front, antes de entregar.
- **Qué ver:** `All files pass linting.`

### `pnpm --prefix frontend run format` · `format:check`
- **Para qué:** `format` **arregla** el formato de todo el front con Prettier; `format:check` sólo
  verifica.
- **Cuándo:** al final, antes de entregar.
- `pnpm --prefix frontend exec prettier --check <carpeta>` revisa una sola carpeta.

### Tests del front (si te sobra tiempo)

| Comando | Para qué / cuándo |
|---|---|
| `pnpm --prefix frontend run test:ci` | Todos los tests de Vitest, una pasada. Para ver que no rompiste nada. |
| `pnpm --prefix frontend test` | Vitest en modo *watch*: se re-ejecuta al guardar. Para trabajar con tests. |
| `pnpm --prefix frontend run e2e` | Playwright, punta a punta en un navegador. Lento; necesita la base. |

---

## 6. Levantar todo para probar (3 terminales)

| Terminal | Comando | Queda corriendo |
|---|---|---|
| 1 | `docker compose up -d` | no (te devuelve la terminal) |
| 2 | `dotnet run --project backend/src/DrinkIt.Api` | **sí** |
| 3 | `pnpm --prefix frontend start` | **sí** |

| Qué | URL |
|---|---|
| Front | `http://localhost:4200` |
| Carta del cliente | `http://localhost:4200/<slug>/menu` |
| Login del personal | `http://localhost:4200/<slug>/staff/login` |
| Scalar | `https://localhost:7176/scalar` |

El `<slug>` y el usuario y clave del admin están en
`backend/src/DrinkIt.Api/appsettings.Development.json`, sección `Bootstrap`.

**Para tener un pedido entregado** (necesita una noche en curso con stock):
admin → crear un KDS y un cajero → crear un producto → crear una noche **que esté en curso ahora** con
ese KDS y cajero → cargar stock en esa KDS → cliente pide (pago digital) y **deja abierto el
seguimiento** → KDS: Preparar → Listo → Entregar.
Si dice "no estamos tomando pedidos": no hay noche en curso o falta stock.

**Atajo:** usar un pedido que ya está en la base (consulta de la sección 4).

---

## 7. Probar endpoints con curl (alternativa a Scalar)

- **Para qué:** llamar a la API desde la terminal.
- **Cuándo:** si Scalar no te anda, o para probar con un token de personal.
- `-k` = aceptar el certificado de desarrollo · `-X POST` = el método · `-H` = un header · `-d` = el body.

```bash
# 1. Login de personal → copiá el "token" de la respuesta
curl -k -X POST https://localhost:7176/<slug>/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"<usuario>","password":"<clave>"}'

# 2. Endpoint de personal: el venue sale del token, la ruta NO lleva slug
curl -k https://localhost:7176/staff/tables -H "Authorization: Bearer <token>"

# 3. Endpoint de cliente: con slug y token del pedido, sin login
curl -k -X POST https://localhost:7176/<slug>/orders/<codigo>/<trackingToken>/rate \
  -H "Content-Type: application/json" -d '{"stars":5,"comment":"genial"}'
```

---

## 8. Buscar en el código (encontrar el "molde")

### `git grep -n "<texto>" [carpeta]`
- **Para qué:** buscar en qué archivos y líneas aparece algo. Sólo busca en archivos versionados, así
  que es rápido y no se mete en `bin/` ni `node_modules`.
- **Cuándo:**
  - para **encontrar un molde**: `git grep -n "MapPost" backend/src/DrinkIt.Api`;
  - para ver **dónde se registra algo**: `git grep -n "AddScoped<CancelOrderHandler>"`;
  - para ver **a quién afecta un cambio**: `git grep -n "OrderStatus.Delivered" backend/src`.
- `-n` muestra el número de línea · `-l` sólo los nombres de archivo.

### Atajos de VS Code

| Atajo | Para qué |
|---|---|
| **Cmd+S** | Guardar. **El compilador lee el disco**: sin guardar, no ve tus cambios. (`File → Auto Save` para no olvidarte) |
| **F2** | Renombrar un símbolo **en todos lados a la vez** |
| **F12** | Ir a la definición |
| **Shift+F12** | Ver todos los usos |
| **Cmd+.** | Arreglo rápido (por ejemplo, agregar el `using` que falta) |
| **Cmd+P** | Abrir un archivo por nombre |
| **Cmd+Shift+F** | Buscar texto en todo el proyecto |

---

## 9. Git

| Comando | Para qué | Cuándo |
|---|---|---|
| `git status` | Qué archivos cambiaste o creaste | Todo el tiempo, para orientarte |
| `git diff` | Ver **línea por línea** qué cambiaste | Antes de dar algo por terminado |
| `git diff --stat` | Resumen: cuántas líneas por archivo | Para detectar cambios gigantes no buscados (como el `schema.d.ts` sin formatear) |
| `git checkout -b feat/60-calificar-pedido` | Crear una rama y pasarte a ella | Al empezar. Prefijo en inglés, descripción en español, sin ñ ni tildes |
| `git add -p` | Agregar cambios **de a pedazos**, revisando cada uno | Antes de commitear |
| `git commit -m "feat(orders): calificar el pedido entregado"` | Guardar un punto en la historia | Al terminar (sólo si te lo piden) |
| `git restore <archivo>` | **Descartar** los cambios de un archivo | Te equivocaste y querés volver a como estaba. ⚠️ No se recupera |
| `git stash` / `git stash pop` | Guardar cambios aparte y recuperarlos | Necesitás cambiar de rama sin perder lo que hiciste |

---

## 10. Cuando algo no anda

| Síntoma | Causa probable → qué hacer |
|---|---|
| "Lo cambié pero no cambia nada" | **No guardaste.** Punto blanco en la pestaña = sin guardar. Cmd+S |
| La API no arranca | Buscá en el log la primera línea `fail:` o `Unhandled exception` |
| `Failure to infer one or more parameters` | Falta registrar el handler en `XxxHandlers.cs` |
| `error CS0246 ... no se encontró` | Falta un `using` → cursor sobre el nombre + **Cmd+.** |
| `Token "." no válido` justo después de un `;` | El `;` cortó una cadena `.AddScoped().AddScoped()`: va sólo al final |
| `no se puede convertir 'X' en 'bool'` en un `Where` | Querías `Select` (`Where` filtra, `Select` transforma) |
| La API no conecta a la base | `docker compose ps` → ¿está `drinkit-sql`? ¿Docker Desktop abierto? |
| `Address already in use` | El puerto está ocupado (quedó otra API o front corriendo). Ver abajo |
| El front no ve el endpoint nuevo | ¿Corriste `generate:api` **con la API levantada**? |
| Se rompieron tests que no tocaste | Agregaste un parámetro **sin default** a un record, o un método a una interfaz |
| TypeScript: "no overlap" en una comparación | Estás comparando con un valor que no existe en ese tipo (mirá el `type`) |
| `dotnet ef`: "no se encontró el comando" | Corré `dotnet tool restore` |

```bash
lsof -i :7176    # quién usa el puerto de la API (para el front: :4200) → te da el PID
kill <PID>       # cortar ese proceso
```

---

## 11. Leer un error de C#

```
RateOrderHandler.cs(8,23): error CS0246: El nombre del tipo 'Result<>' no se encontró (¿falta una directiva using?)
         │          │  │         │
       archivo   línea col    código       ← el mensaje casi siempre trae la pista
```

Si el error aparece en una línea que parece estar bien, mirá **la línea de arriba** (un `;` o una `}`
de más o de menos).

---

## 12. Checklist antes de entregar

```bash
dotnet build backend/DrinkIt.slnx                     # todo compila, tests incluidos
dotnet test backend/tests/DrinkIt.ArchitectureTests   # reglas de arquitectura
dotnet format backend/DrinkIt.slnx                    # formato C#
pnpm --prefix frontend run build                      # el front compila
pnpm --prefix frontend run lint                       # reglas del front
pnpm --prefix frontend run format                     # formato del front
git status                                            # ¿están todos los archivos que esperás?
```

- [ ] Todo el código en inglés; sólo los textos de pantalla en español (de vos).
- [ ] Ningún `if` de negocio en el endpoint.
- [ ] Rutas de personal sin `{venueSlug}`; rutas de cliente con `{venueSlug}` + `ScopedBySlugAttribute`.
- [ ] Entidad nueva: `IBelongsToVenue` + `HasQueryFilter` + `AuditStamps` + sin setters públicos.
- [ ] Índices únicos compuestos con `VenueId`.
- [ ] Migración revisada; `schema.d.ts` formateado.
