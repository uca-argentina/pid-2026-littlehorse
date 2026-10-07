### Sprint 3

- Administrar eventos y barras. Cada producto puede estar disponible en una o más barras.
- Incorporar stock por barra y evitar confirmar pedidos sin unidades suficientes.
- Agregar roles Cajero y Mozo.
- Permitir pedidos con retiro en barra o entrega a mesa VIP.
- Las mesas VIP tienen saldo de consumo. Si no alcanza, el cliente puede cubrir la diferencia con un pago simulado.
- El mozo visualiza pedidos VIP listos y los marca como entregados.
- Alertar pedidos con espera excesiva y mostrar métricas de tiempos, ventas simuladas y productos más solicitados.

---

## Interpretación nuestra

> Esto **no** es parte del enunciado. Va acá abajo y marcado, como dice el
> [README de sprints](README.md).

A diferencia del Sprint 2, este enunciado **sí fija el alcance**: trae siete puntos
funcionales, como el Sprint 1. No dice nada del formato de las stories, así que seguimos con el
del Sprint 2 (narrativa de una línea, **entre 2 y 4 criterios** y notas). Nos sirvió y no hay
razón para cambiarlo.

Cómo leemos cada punto, en una línea. El detalle está en el
[backlog](sprint-3-backlog.md):

1. **Eventos y barras.** Un evento es **una noche** del local, y organiza todo lo de esa noche:
   horario, qué KDS andan, quiénes trabajan y con qué stock se arranca. **Una barra es una
   KDS**: las cuentas de KDS ya se dan de alta, y lo que falta es que varias anden a la vez
   recibiendo pedidos distintos.
2. **Stock.** No se parte por barra (decidido el 2026-10-06): todas las KDS sirven del mismo
   stock, que pasa a ser **el de la noche**. Confirmar ya rechaza un pedido sin unidades; eso se
   mantiene.
3. **Cajero y Mozo.** El cajero ya existe desde US-26. El mozo existe como rol y ya se le puede
   crear la cuenta, pero no tiene pantalla: lo que falta es eso.
4. **Retiro en barra o mesa VIP.** El tipo de entrega sale de cómo entró el cliente: con el
   código de una mesa, se la llevan; sin él, la retira en la barra.
5. **Saldo VIP y diferencia.** Cierra la pregunta abierta del §8.1 del diseño funcional por la
   opción que ya preferíamos: cubrir la diferencia con otro medio de pago.
6. **El mozo.** Su pantalla está dibujada (`MozoEntregas`).
7. **Alertas y métricas.** Las alertas son para quien está a cargo esa noche; las métricas se
   miran por noche.
