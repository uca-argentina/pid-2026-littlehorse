### Sprint 4

- Agregar Gamification.
- Crear combos compuestos por varios productos. Al confirmar un combo, descontar el stock de cada componente en la barra correspondiente.
- Configurar promociones por franja horaria (por ejemplo, happy hour) aplicadas automáticamente al confirmar el pedido.

---

## Interpretación nuestra

> Esto **no** es parte del enunciado. Va acá abajo y marcado, como dice el
> [README de sprints](README.md).

El enunciado fija el alcance con tres puntos funcionales. No dice nada del formato de las
stories, así que seguimos con el del Sprint 2 y el Sprint 3: narrativa de una línea, **entre 2 y 4
criterios** y notas.

Cómo leemos cada punto, en una línea. El detalle está en el
[backlog](sprint-4-backlog.md):

1. **Gamification.** El enunciado no dice a quién apunta ni cómo. Decidido el 2026-11-04: es una
   **tarjeta de sellos del cliente** ("cada N tragos, uno de regalo"), sin cuenta, reconocida por
   un token guardado en el celular.
2. **Combos.** Un combo es un producto de la carta que no tiene stock propio: se vende desde el
   stock de sus componentes. Un pedido va a **una sola barra** (US-36), así que "la barra
   correspondiente" es esa, y tiene que tener todos los componentes.
3. **Promociones.** Se repiten **por semana, por día y hora** (decidido el 2026-11-04), con un
   porcentaje de descuento sobre productos elegidos. El precio se congela al confirmar, como ya
   se hace con `UnitPrice`.
