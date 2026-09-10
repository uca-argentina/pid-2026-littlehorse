# drink.it

[![CI backend](https://github.com/lamelapablo/drink-it/actions/workflows/ci-backend.yml/badge.svg)](https://github.com/lamelapablo/drink-it/actions/workflows/ci-backend.yml)
[![CI frontend](https://github.com/lamelapablo/drink-it/actions/workflows/ci-frontend.yml/badge.svg)](https://github.com/lamelapablo/drink-it/actions/workflows/ci-frontend.yml)

**Pedí tu trago desde el celular y retiralo cuando esté listo.** PWA de gestión de pedidos
para boliches: el cliente pide y paga desde donde esté, el bartender prepara con un KDS en
tablet, y el retiro es asíncrono.

## El problema

En un boliche se pierde mucho tiempo en filas. Hay dos modelos y los dos son malos:

- **Modelo A** — una fila para pagar en caja y otra en la barra para que te lo preparen.
- **Modelo B** — una fila única donde cobran y preparan al mismo tiempo.

En los dos casos el cliente espera parado mientras le preparan el trago. drink.it corta eso:
pedís y pagás desde el celular sin acercarte a la barra, seguís en lo tuyo, y vas a retirar
recién cuando te avisa una notificación push.

No hay que instalar nada desde una store para pedir: la PWA funciona desde el navegador.
Recién después de pagar se sugiere agregarla a la pantalla de inicio, que es el momento de
mayor motivación y lo que habilita el push.

## Estado

> **Sprint 1 en curso** — entrega el 17 de septiembre de 2026. El alcance comprometido está
> en el [backlog](docs/sprints/sprint-1-backlog.md).
>
> Listo y con tests: el modelo de local y personal, el aislamiento entre locales, el ingreso
> del personal (caso de uso y endpoint) y la semilla de desarrollo. Falta todo el frontend:
> la PWA todavía no tiene ninguna pantalla.

## Stack

| Capa | Tecnología |
|---|---|
| Backend | .NET 10 · Minimal APIs · EF Core · SignalR |
| Frontend | Angular 22 (standalone + signals) · PWA (`@angular/service-worker`) |
| Base de datos | Azure SQL — SQL Server en Docker para desarrollo local |
| Tiempo real | SignalR in-process (sin Azure SignalR Service) |
| Notificaciones | Web Push (VAPID) desde el Service Worker |
| Hosting | Azure Container Apps · Static Web Apps · Azure SQL — todo en tiers gratuitos |
| CI/CD | GitHub Actions con OIDC hacia Azure |
| Testing | xUnit · Testcontainers · Vitest · Playwright |

Se eligió Angular sobre Blazor por el peso del primer load: el cliente abre la app una sola
vez desde un QR con datos móviles saturados. El razonamiento completo va en `docs/adr/`.

## Estructura

```
backend/          # Solución .NET. Domain, Application, Infrastructure, Api + tests.
frontend/         # PWA Angular y specs de Playwright.
design/           # Wireframes de las pantallas.
docs/             # Diseño funcional, modelo de datos, ADRs y consignas.
infra/            # Bicep (todavía vacío).
.github/          # Workflows de CI.
.claude/          # Configuración compartida de Claude Code (ver .claude/README.md).
.vscode/          # Settings y extensiones recomendadas del equipo.
```

## Empezar a desarrollar

### Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (LTS)
- [Node.js](https://nodejs.org/) `^22.22.3`, `^24.15.0` o `>=26` — lo exige Angular 22
- [pnpm](https://pnpm.io/) — `npm install -g --allow-scripts=pnpm pnpm@latest`. El
  `--allow-scripts` hace falta: sin él npm bloquea el script que arma el ejecutable de pnpm
  en Windows. Corepack no sirve, Node dejó de distribuirlo a partir de la v25.
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — para SQL Server local
  y los tests de integración con Testcontainers
- VS Code con las extensiones recomendadas (te las ofrece al abrir el workspace, o buscá
  `@recommended` en el panel de extensiones)

### Setup

```bash
git clone https://github.com/lamelapablo/drink-it.git
cd drink-it
pnpm --prefix frontend install
dotnet restore backend/DrinkIt.slnx
docker compose up -d                    # SQL Server local, en localhost,1433
```

La base tiene que estar levantada **antes** de correr la API: la cadena de conexión apunta a
ese contenedor. En Development la API se aplica las migraciones y siembra un boliche con un
administrador sola, así que después de `docker compose up -d` no hay ningún paso manual.

Los tests no usan ese contenedor: los de integración levantan el suyo con Testcontainers y lo
tiran al terminar. Sólo necesitan Docker corriendo.

La versión exacta del SDK de .NET sale de `global.json` y la de pnpm del campo
`packageManager` de `frontend/package.json`. No hace falta elegirlas: las herramientas las
leen solas, y son las mismas que usa la CI.

### Comandos

| | Backend | Frontend |
|---|---|---|
| Levantar la base | `docker compose up -d` | — |
| Arrancar | `dotnet run --project backend/src/DrinkIt.Api` | `pnpm --prefix frontend start` |
| Tests | `dotnet test backend/DrinkIt.slnx` | `pnpm --prefix frontend test` |
| Tests (una vez, sin watch) | — | `pnpm --prefix frontend run test:ci` |
| Lint | `dotnet format backend/DrinkIt.slnx --verify-no-changes` | `pnpm --prefix frontend run lint` |
| Arreglar formato | `dotnet format backend/DrinkIt.slnx` | `pnpm --prefix frontend run format` |
| Build de producción | `dotnet build backend/DrinkIt.slnx -c Release` | `pnpm --prefix frontend run build --configuration production` |

Son exactamente los que corre la CI. Si pasan en tu máquina, pasan en el pipeline.

## Cómo trabajamos

Antes de tu primer PR, leé:

| Documento | Qué te dice |
|---|---|
| [CLAUDE.md](CLAUDE.md) | Arquitectura, reglas de dependencias, convenciones y Definition of Done. |
| [.claude/README.md](.claude/README.md) | Cómo usamos Claude Code en el equipo. |
| [docs/adr/](docs/adr/) | Por qué tomamos las decisiones técnicas que tomamos. |

Lo esencial:

- **TDD estricto.** El test que falla va primero. Nada de implementación sin un test que la exija.
- **Flujo de ramas:** `feature` → PR a `dev` → PR a `main`. Nunca un feature directo a `main`.
  Squash al mergear a `dev`, merge commit de `dev` a `main`. Detalle completo en `CLAUDE.md`.
  Máximo dos PRs abiertos a la vez y se revisan el mismo día: con tres personas y diez días,
  un PR esperando cuarenta y ocho horas hace más daño que cualquier problema técnico.
- **Conventional Commits** (`feat:`, `fix:`, `test:`, `refactor:`, `chore:`).
- **Código en inglés, documentación en español.** El glosario de traducción está en `CLAUDE.md`.
- **Sólo dependencias gratuitas y open source.** Se proponen con la licencia verificada.

## Documentación

- [Diseño funcional](docs/drink.it.v2.md) — la fuente de verdad del comportamiento del sistema.
- [Flujo del pedido](docs/flujo-pedido.md) — diagrama de secuencia de punta a punta.
- [Modelo de datos](docs/modelo-de-datos.md) — ERM y máquina de estados del Sprint 1.
- [Consignas](docs/sprints/) — el enunciado de cada sprint, tal cual lo entregó el cliente.
- [Decisiones de arquitectura](docs/adr/) — ADRs.

`docs/historico/` guarda versiones superadas del diseño. **No las uses como referencia.**

## Equipo

- Pablo Lamela — [@lamelapablo](https://github.com/lamelapablo)
- Eugenia Quadro — [@eugeqq](https://github.com/eugeqq)
- Nicolás Coloritto [@nicocoloritto](https://github.com/nicocoloritto)

## Licencia

Propietario — todos los derechos reservados. Ver [LICENSE](LICENSE).

No es open source **a propósito**: la decisión se puede revertir hacia abrir el código, pero
no al revés. El ADR-0005 que la registra todavía está pendiente, junto con los otros que
lista [docs/adr/README.md](docs/adr/README.md).
