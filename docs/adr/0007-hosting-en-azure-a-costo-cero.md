# ADR-0007: Hosting en Azure a costo cero

- **Estado:** aceptada
- **Fecha:** 2026-09-06

## Contexto

El proyecto no tiene presupuesto: el requisito es **costo cero, sin excepciones**. No se
acepta "unos dólares por mes" ni un tier gratuito que caduque a los 12 meses.

Además se decidió tener **un solo ambiente desplegado**, alimentado desde `main`. La rama
`dev` queda como integración, sin deploy asociado.

El stack a hostear es: una PWA de Angular (estática, sin SSR), una API .NET 10 con SignalR
in-process, y una base relacional.

## Opciones evaluadas

### Para la API

| Opción | Cómputo gratis mensual | Problema |
|---|---|---|
| **App Service F1** | 60 minutos de CPU **por día** (~30 h/mes) | Al superar la cuota diaria devuelve `403` hasta la medianoche UTC. No escala a cero: se duerme, con arranque en frío de 10-20 s. |
| **Container Apps** (Consumption) | 180.000 vCPU-seg + 360.000 GiB-seg + 2M requests **por suscripción, por mes** | Exige imagen de contenedor y un registry. |
| **Azure Functions** | 1M ejecuciones | SignalR y una API con estado no encajan en el modelo. |

A 0,25 vCPU y 0,5 GiB —la configuración mínima— el grant de Container Apps equivale a unas
**200 horas de ejecución por mes**, casi 7× lo de App Service F1, y sin el precipicio diario.

### Para la base de datos

El **free offer de Azure SQL** da 100.000 vCore-segundos de cómputo serverless, 32 GB de
datos y 32 GB de backup por base y por mes, **durante toda la vida de la suscripción** y
hasta 10 bases. No es un trial: no caduca.

Se descartó PostgreSQL Flexible Server porque su tier gratuito es un trial de 12 meses, y
Cosmos DB porque es NoSQL y el modelo es relacional.

### Para el registry

**ACR Basic cuesta ~USD 5/mes**, así que queda descartado. `ghcr.io` es gratuito y Container
Apps puede tirar imágenes de cualquier registry.

## Decisión

| Componente | Recurso |
|---|---|
| PWA | Static Web Apps, tier Free |
| API | Container Apps, perfil **Consumption**, `minReplicas: 0` |
| Imagen | GitHub Container Registry (`ghcr.io`) |
| Base de datos | Azure SQL, free offer, serverless con auto-pausa |
| Tiempo real | SignalR in-process, **sin** Azure SignalR Service |
| Observabilidad | Application Insights (5 GB/mes gratis) |

**Azure SignalR Service queda explícitamente afuera.** Existe para repartir conexiones entre
varias instancias; con una sola es un recurso de más que hay que crear, configurar y pagar
sin ganar nada. Se reconsidera si alguna vez hace falta escalar horizontalmente.

## La garantía real de costo cero

No depende de acertarle al SKU. **Azure for Students tiene límite de gasto y no lleva tarjeta
de crédito asociada**: cuando el crédito se agota, Azure deshabilita la suscripción en vez de
facturar. El peor caso posible es "se apagó la app", nunca "llegó una factura".

Elegir bien los tiers es lo que hace que el crédito ni se toque; el límite de gasto es lo que
garantiza que no haya sorpresas si nos equivocamos.

## Consecuencias

**Lo que ganamos:** costo cero verificable, escalado a cero real, y una base de datos gratuita
que no caduca.

**Lo que perdemos:** hay que mantener un `Dockerfile` y un paso de build/push en la CI. Con
App Service alcanzaba un `dotnet publish`.

**Tres riesgos conocidos:**

1. **SignalR pelea con el escalado a cero.** Una conexión WebSocket abierta mantiene vivo el
   contenedor y consume el grant. Irrelevante en una demo; importa si algo queda corriendo
   días.
2. **Arranque en frío doble.** Con `minReplicas: 0` el contenedor tarda segundos en despertar,
   y Azure SQL serverless con auto-pausa tarda **30-60 segundos**. Hay que calentar ambos
   antes de mostrar algo.
3. **Log Analytics.** Container Apps crea un workspace por defecto; tiene 5 GB/mes gratis pero
   conviene bajar la retención para no acercarse al límite.

## Qué no pudimos verificar

Los límites citados salen de la documentación oficial de Microsoft a la fecha de este ADR.
Las ofertas gratuitas de Azure cambian: conviene reverificarlas antes de crear los recursos,
y no asumir que siguen vigentes dentro de seis meses.
