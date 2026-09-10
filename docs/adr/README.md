# Architecture Decision Records

Una decisión técnica que cueste revertir se escribe acá. No es burocracia: dentro de tres
meses nadie se va a acordar por qué elegimos lo que elegimos, y sin el registro la discusión
se vuelve a dar entera.

Numerá secuencialmente: `0001-titulo-corto.md`.

## Decisiones tomadas y todavía sin registrar

Estas ya se decidieron en conversación y les falta el ADR:

- **0001** — Angular en vez de Blazor para la PWA.
- **0002** — Repositorio sólo por agregado con invariantes; lecturas con EF Core directo.
- **0003** — .NET 10 (LTS) en vez de .NET 9, que sale de soporte el 10/11/2026.
- **0004** — Multi-tenant desde el día uno, con base compartida y columna discriminadora.
- **0005** — Licencia propietaria en vez de open source.
- **0006** — Zoneless: es el default de Angular 22 y lo mantenemos.

## Decisiones ya registradas

- [ADR-0007 — Hosting en Azure a costo cero](0007-hosting-en-azure-a-costo-cero.md)
- [ADR-0008 — Autenticación con token único, sin refresh token](0008-autenticacion-con-token-unico-sin-refresh-token.md)
- [ADR-0009 — Los errores de la API viajan como Problem Details (RFC 9457)](0009-errores-de-la-api-como-problem-details.md)

## Plantilla

```markdown
# ADR-XXXX: <decisión>

- **Estado:** propuesta | aceptada | reemplazada por ADR-YYYY
- **Fecha:** AAAA-MM-DD

## Contexto
Qué problema concreto de drink.it nos trae acá.

## Opciones evaluadas
Para cada una: qué es, licencia, costo, y el trade-off real.

## Decisión
Cuál y por qué, atada a las restricciones del proyecto.

## Consecuencias
Qué ganamos, qué perdemos, y qué se vuelve difícil después de esto.
```

El agente `investigador-tecnico` devuelve su salida en este formato.
