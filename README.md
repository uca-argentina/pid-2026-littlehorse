# drink.it

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

> **En bootstrap.** El repositorio tiene la configuración del equipo, los estándares y el
> diseño funcional. Todavía no hay código de aplicación — el scaffold de `backend/` y
> `frontend/` es el próximo paso.

## Stack

| Capa | Tecnología |
|---|---|
| Backend | .NET 10 · Minimal APIs · EF Core · SignalR |
| Frontend | Angular 22 (standalone + signals) · PWA (`@angular/service-worker`) |
| Base de datos | Azure SQL — SQL Server en Docker para desarrollo local |
| Tiempo real | SignalR / Azure SignalR Service |
| Notificaciones | Web Push (VAPID) desde el Service Worker |
| Hosting | Azure App Service · Static Web Apps · Key Vault |
| CI/CD | GitHub Actions con OIDC hacia Azure |
| Testing | xUnit · Testcontainers · Vitest · Playwright |

Se eligió Angular sobre Blazor por el peso del primer load: el cliente abre la app una sola
vez desde un QR con datos móviles saturados. El razonamiento completo va en `docs/adr/`.

## Estructura

```
backend/          # Solución .NET. Domain, Application, Infrastructure, Api + tests.
frontend/         # PWA Angular y specs de Playwright.
infra/            # Bicep.
docs/             # Diseño funcional, flujos y ADRs.
.claude/          # Configuración compartida de Claude Code (ver .claude/README.md).
.vscode/          # Settings y extensiones recomendadas del equipo.
```

## Empezar a desarrollar

### Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (LTS)
- [Node.js](https://nodejs.org/) `^22.22.3`, `^24.15.0` o `>=26` — lo exige Angular 22, y npm
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — para SQL Server local
  y los tests de integración con Testcontainers
- VS Code con las extensiones recomendadas (te las ofrece al abrir el workspace, o buscá
  `@recommended` en el panel de extensiones)

### Setup

```bash
git clone <url-del-repo>
cd drink-it
```

> Los comandos de build, test y arranque se agregan acá junto con el scaffold.

## Cómo trabajamos

Antes de tu primer PR, leé:

| Documento | Qué te dice |
|---|---|
| [CLAUDE.md](CLAUDE.md) | Arquitectura, reglas de dependencias, convenciones y Definition of Done. |
| [.claude/README.md](.claude/README.md) | Cómo usamos Claude Code en el equipo. |
| [docs/adr/](docs/adr/) | Por qué tomamos las decisiones técnicas que tomamos. |

Lo esencial:

- **TDD estricto.** El test que falla va primero. Nada de implementación sin un test que la exija.
- **Nunca se commitea directo a `main`.** Rama `feat/<issue>-descripcion-corta` y PR con una aprobación.
- **Conventional Commits** (`feat:`, `fix:`, `test:`, `refactor:`, `chore:`).
- **Código en inglés, documentación en español.** El glosario de traducción está en `CLAUDE.md`.
- **Sólo dependencias gratuitas y open source.** Se proponen con la licencia verificada.

## Documentación

- [Diseño funcional](docs/drink.it.v2.md) — la fuente de verdad del comportamiento del sistema.
- [Flujo del pedido](docs/flujo-pedido.md) — diagrama de secuencia de punta a punta.
- [Decisiones de arquitectura](docs/adr/) — ADRs.

`docs/historico/` guarda versiones superadas del diseño. **No las uses como referencia.**

## Equipo

- Pablo Lamela — [@lamelapablo](https://github.com/lamelapablo)
- Eugenia Quadro — [@eugeqq](https://github.com/eugeqq)
- Nicolás Coloritto

## Licencia

Propietario — todos los derechos reservados. Ver [LICENSE](LICENSE).

No es open source **a propósito**: la decisión se puede revertir hacia abrir el código, pero
no al revés. Ver `docs/adr/` cuando esté escrito el ADR correspondiente.
