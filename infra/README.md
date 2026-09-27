# Infra de drink.it

Bicep para todo lo que corre en Azure. Ver
[ADR-0007](../docs/adr/0007-hosting-en-azure-a-costo-cero.md) para el porqué de cada SKU.

## Qué se creó ya, a mano (no está en el Bicep)

Esto es identidad y permisos, no recursos de la app — no tiene sentido recrearlo en cada
deploy, así que se hizo una sola vez con `az cli`:

- Resource group `rg-drinkit` en `brazilsouth`.
- App Registration `drinkit-github-actions` (appId `028b0948-3521-49d9-9029-e6c02e8052b2`)
  con federated credential OIDC `github-pid-2026-littlehorse-production` para el subject
  `repo:uca-argentina@42899167/pid-2026-littlehorse@1370630407:environment:production`.
  Sin secretos de larga vida: GitHub Actions autentica con un token de corta vida en cada
  corrida.
    - El subject lleva `environment:production` y no `ref:refs/heads/main` porque los jobs de
      deploy declaran `environment: production`, y en ese caso GitHub emite el token con el
      environment. Lo que impide deployar desde otra rama es la regla del environment, que
      sólo admite `main`.
    - El repo usa el formato de subject **inmutable**, con los IDs numéricos de la org y del
      repo. El prefijo exacto sale de
      `gh api repos/uca-argentina/pid-2026-littlehorse/actions/oidc/customization/sub`.
      Por eso el credential se crea con el escenario "Other issuer" del portal: el de
      "GitHub Actions" arma el subject sin los IDs y no coincide.
- Ese service principal tiene rol **Contributor** sobre `rg-drinkit`, nada más amplio.

Environment `production` en GitHub (Settings → Environments), con **Deployment branches**
restringido a `main`. Los secrets van en el environment, no a nivel repo: así sólo los
leen los jobs de deploy que corren desde `main`.

| Secret                     | Valor                                                                              |
| -------------------------- | ---------------------------------------------------------------------------------- |
| `AZURE_CLIENT_ID`          | `028b0948-3521-49d9-9029-e6c02e8052b2`                                             |
| `AZURE_TENANT_ID`          | `9c25874e-f68b-41d2-8a38-3b7aa9f60cda`                                             |
| `AZURE_SUBSCRIPTION_ID`    | `9cce0d2e-78aa-4cd7-b828-12a7f1af28c4`                                             |
| `JWT_SIGNING_KEY`          | generada una vez con `openssl rand -base64 48`, no la de dev                       |
| `BOOTSTRAP_ADMIN_PASSWORD` | contraseña del primer administrador; sólo hasta el primer login (ver abajo)        |
| `GHCR_PULL_USERNAME`       | usuario de GitHub dueño del token de abajo                                         |
| `GHCR_PULL_TOKEN`          | token clásico con `read:packages`; sólo mientras la imagen sea privada (ver abajo) |

Y una **variable** (no secret) del mismo environment, opcional:

| Variable                 | Valor                                                                                      |
| ------------------------ | ------------------------------------------------------------------------------------------ |
| `FRONTEND_CUSTOM_DOMAIN` | dominio propio del PWA, sin `https://` (p. ej. `drinkit.example.com`); vacía hasta tenerlo |

La API sólo acepta llamadas del navegador desde el hostname de la Static Web App y, si está
cargada, desde ese dominio (ver la adenda del 2026-09-27 en ADR-0007). Al registrar el
dominio: agregarlo como custom domain en la Static Web App, cargar la variable y volver a
deployar. Sin la variable, el PWA servido desde el dominio nuevo recibe errores de CORS.

## Decisiones de esta primera versión

- **Azure SQL con AAD-only auth, sin contraseña en ningún lado.** El admin del server es
  la cuenta de Entra ID de Pablo (`sqlAadAdminObjectId`/`sqlAadAdminLoginName` en
  `main.bicep`). La API se conecta con la **user-assigned managed identity** `id-drinkit-api`
  vía `Authentication=Active Directory Managed Identity` en la cadena de conexión — no hay
  password que rotar ni que filtrar.
- **La imagen de la API en ghcr.io tiene que ser pública.** Container Apps no tiene forma
  de autenticarse contra GHCR con Managed Identity, y pedirle un PAT de GitHub como secret
  sólo para bajar una imagen es más superficie de ataque que exponerla. El código fuente
  privado no se toca — sólo el binario compilado. Se hace a mano una vez: en GitHub,
  paquete `pid-2026-littlehorse-api` → **Package settings → Change visibility → Public**.
    - **Mientras tanto, privada con token.** La organización `uca-argentina` no permite
      paquetes públicos, así que por ahora el Container App baja la imagen con
      `GHCR_PULL_USERNAME` y `GHCR_PULL_TOKEN`: un token clásico de GitHub con sólo
      `read:packages`. Es un secreto de larga vida atado a una cuenta personal: si vence o esa
      persona deja la organización, el pull falla. El día que la imagen sea pública, borrar los
      dos secrets alcanza: con el token vacío el Bicep no configura ningún registry.
- **`storage` y `Jwt:SigningKey` sí son secrets del Container App**, no hay forma AAD-only
  para esos dos con el código actual (ver ADR-0007, adenda de las fotos). Van como
  `secretRef`, nunca como variable de entorno plana.

## Paso manual que falta: alta del usuario de base de datos

Bicep no tiene un recurso para crear un **contained user** dentro de la base — eso es TSQL,
no ARM. Sin este paso la API se conecta a Azure SQL pero no tiene permisos y todo falla con
`Login failed`. Correrlo **una sola vez**, después del primer deploy, contra `drinkit` en
`sql-drinkit-<sufijo>.database.windows.net` (el sufijo sale del output `sqlServerFqdn` del
deployment), autenticado con la cuenta de Entra ID admin (portal → Query editor, o
`sqlcmd -S <fqdn> -d drinkit -G`):

```sql
CREATE USER [id-drinkit-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-drinkit-api];
ALTER ROLE db_datawriter ADD MEMBER [id-drinkit-api];
ALTER ROLE db_ddladmin ADD MEMBER [id-drinkit-api];
```

`db_ddladmin` es necesario porque la API corre las migraciones ella misma al arrancar, en
todo ambiente — ver la próxima sección.

> ponytail: paso manual de una sola vez, no un `deploymentScript` de Bicep. Automatizarlo si
> alguna vez hay que recrear el ambiente seguido (hoy es "una vez y listo").

## Primer administrador

No hay pantalla de registro, así que el primer usuario lo crea la API al arrancar
(`BootstrapSeeder`): el boliche `bar-alfa` ("Bar Alfa") y el usuario `admin`, con rol
Administrador. Sólo lo hace si tiene `Bootstrap__AdminPassword`; sin esa variable no toca
nada. Si el admin ya existe tampoco: cambiar el secret después no cambia la contraseña.

1. Cargar `BOOTSTRAP_ADMIN_PASSWORD` en el environment `production` con una contraseña
   fuerte. El workflow la pasa al Bicep, que la agrega al Container App como `secretRef`.
2. Deployar y correr el TSQL de arriba. La API siembra al arrancar, y antes del TSQL no
   tiene permisos en la base: si el primer arranque falló, reiniciar la revisión del
   Container App. Después, entrar con `admin` en `/bar-alfa/staff/login`.
3. Borrar el secret de GitHub. En el próximo deploy el Bicep saca la variable del
   Container App, y la contraseña deja de vivir en Azure.

4. Que termine de registrarse `Microsoft.App` y `Microsoft.Sql` en la suscripción
   (`az provider show -n Microsoft.App --query registrationState`).
5. Crear el environment `production` y cargarle los 4 secrets de la tabla de arriba.
6. Mergear a `main` → dispara `.github/workflows/deploy-main.yml`.
7. Correr el TSQL de arriba una vez.
8. Poner pública la imagen en GHCR (o el primer arranque del contenedor falla al no poder
   pullearla).

## Migraciones de EF en producción

Decidido: la API las corre sola al arrancar, en todo ambiente (`Program.cs`, sin el guard de
`IsDevelopment()` que tenía antes). Se eligió por sobre un paso separado en la CI porque acá
hay una sola sede con tráfico bajo — no justifica una segunda identidad con permisos sobre
la base sólo para correr migraciones desde GitHub Actions. El riesgo aceptado: con
`minReplicas: 0` / `maxReplicas: 2`, dos réplicas pueden arrancar a la vez en un cold start y
las dos intentan migrar — EF Core serializa eso con un lock en la tabla de historial de
migraciones, así que la perdedora espera en vez de romper el schema. Si el tráfico crece y
esto deja de ser cierto, ahí sí vale la pena mover esto a un paso explícito de la CI.
