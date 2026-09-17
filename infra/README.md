# Infra de drink.it

Bicep para todo lo que corre en Azure. Ver
[ADR-0007](../docs/adr/0007-hosting-en-azure-a-costo-cero.md) para el porqué de cada SKU.

## Qué se creó ya, a mano (no está en el Bicep)

Esto es identidad y permisos, no recursos de la app — no tiene sentido recrearlo en cada
deploy, así que se hizo una sola vez con `az cli`:

- Resource group `rg-drinkit` en `brazilsouth`.
- App Registration `drinkit-github-actions` (appId `028b0948-3521-49d9-9029-e6c02e8052b2`)
  con federated credential OIDC para `repo:lamelapablo/drink-it:ref:refs/heads/main`.
  Sin secretos de larga vida: GitHub Actions autentica con un token de corta vida en cada
  corrida.
- Ese service principal tiene rol **Contributor** sobre `rg-drinkit`, nada más amplio.

Secrets que tiene que tener el repo en GitHub (Settings → Secrets and variables → Actions):

| Secret                  | Valor                                                        |
| ----------------------- | ------------------------------------------------------------ |
| `AZURE_CLIENT_ID`       | `028b0948-3521-49d9-9029-e6c02e8052b2`                       |
| `AZURE_TENANT_ID`       | `9c25874e-f68b-41d2-8a38-3b7aa9f60cda`                       |
| `AZURE_SUBSCRIPTION_ID` | `9cce0d2e-78aa-4cd7-b828-12a7f1af28c4`                       |
| `JWT_SIGNING_KEY`       | generada una vez con `openssl rand -base64 48`, no la de dev |

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
  paquete `drink-it-api` → **Package settings → Change visibility → Public**.
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

## Qué falta para que el primer deploy funcione de punta a punta

1. Que termine de registrarse `Microsoft.App` y `Microsoft.Sql` en la suscripción
   (`az provider show -n Microsoft.App --query registrationState`).
2. Cargar los 4 secrets de GitHub de la tabla de arriba.
3. Mergear a `main` → dispara `.github/workflows/deploy-main.yml`.
4. Correr el TSQL de arriba una vez.
5. Poner pública la imagen en GHCR (o el primer arranque del contenedor falla al no poder
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
