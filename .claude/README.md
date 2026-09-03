# Cómo usamos Claude Code en drink.it

Esta carpeta se commitea. Es configuración compartida del equipo: si alguien mejora una
skill, mejora para los tres. Trátenla como código — pasa por PR.

## Qué hay acá

| Archivo | Qué hace |
|---|---|
| `../CLAUDE.md` | Contexto del proyecto. Se carga solo en **cada** sesión. Es lo que evita tener que re-explicar la arquitectura todos los días. |
| `settings.json` | Permisos (qué puede correr sin preguntar, qué está prohibido) y el hook de formateo. |
| `agents/` | Subagentes especializados. Corren en su propio contexto y devuelven una conclusión. |
| `skills/` | Workflows del equipo. Se activan solos cuando la tarea encaja, o los invocás con `/`. |
| `hooks/format-archivo.ps1` | Formatea automáticamente cada archivo que Claude edita. |
| `../.mcp.json` | Servidores MCP del proyecto (Playwright, GitHub, Azure). |

## Los agentes

Se invocan pidiéndolos por nombre. Corren aparte, así que la exploración pesada no te
ensucia el contexto de la conversación principal.

- **`revisor-arquitectura`** — antes de abrir un PR que toque `backend/`. Busca violaciones
  reales de la regla de dependencias, SOLID y dominio anémico. Sólo lee.
- **`revisor-angular`** — para PRs de `frontend/`. Change detection, service worker, offline,
  accesibilidad, y usabilidad en el contexto real (de noche, con una mano, con mala señal).
- **`investigador-tecnico`** — antes de agregar una dependencia o comprometerse con un
  servicio de Azure. Devuelve un borrador de ADR, verifica licencias y costos.

Ejemplo: `usá revisor-arquitectura sobre el diff de esta rama`

## Las skills

Se disparan solas cuando la tarea encaja con su descripción. También podés invocarlas con
`/feature-vertical`, `/endpoint-api`, etc.

- **`feature-vertical`** — implementar una feature completa dominio → API → PWA → E2E, en TDD.
- **`endpoint-api`** — endpoints .NET: forma, mapeo de errores a `ProblemDetails`,
  idempotencia, OpenAPI.
- **`pantalla-angular`** — pantallas Angular: standalone + signals + Resource API, los cuatro
  estados, accesibilidad.
- **`spec-e2e`** — specs de Playwright, incluidos los recorridos multi-actor.

## Comandos que más van a usar

| Comando | Para qué |
|---|---|
| `/code-review` | Revisión del diff actual buscando bugs. Distinto de los revisores de arriba: ese busca arquitectura, este busca correctitud. |
| `/security-review` | Antes de mergear cualquier cosa que toque pagos, saldo VIP o autorización. |
| `/init` | Sólo si hay que regenerar `CLAUDE.md` desde cero. |
| `Shift+Tab` | Cicla el modo de permisos. **Plan mode** para diseñar sin que toque archivos. |
| `Esc` (doble) | Volver atrás en la conversación cuando arrancó para el lado equivocado. |
| `/clear` | Entre tareas no relacionadas. Contexto sucio = respuestas peores y más caras. |

## Reglas de convivencia con la herramienta

1. **Un chat por tarea.** No arrastres el contexto de una feature a la siguiente.
2. **Plan mode primero** en cualquier cosa que toque más de dos archivos. Revisás el plan,
   lo corregís, y recién ahí le das vía libre. Corregir un plan cuesta un minuto; corregir
   una implementación de 400 líneas cuesta una tarde.
3. **Los tests los leés vos.** Que estén en verde no significa que testeen lo correcto.
   Es el único control de calidad que no se puede delegar.
4. **Si Claude propone una dependencia nueva, va a `investigador-tecnico` y después al
   grupo.** Somos tres, cada dependencia es deuda compartida.
5. **Cuando corrijas a Claude sobre una convención del proyecto, actualizá `CLAUDE.md`.**
   Si lo corregís y no lo escribís, lo vas a corregir de nuevo mañana, y tus compañeros
   también.

## Configuración personal

`settings.local.json` (gitignoreado) es para lo tuyo: permisos extra, modelo preferido.
No metas preferencias personales en `settings.json` — ese es del equipo.
