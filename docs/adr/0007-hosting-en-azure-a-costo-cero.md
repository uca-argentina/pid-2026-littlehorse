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

| Opción                           | Cómputo gratis mensual                                                        | Problema                                                                                                                         |
| -------------------------------- | ----------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| **App Service F1**               | 60 minutos de CPU **por día** (~30 h/mes)                                     | Al superar la cuota diaria devuelve `403` hasta la medianoche UTC. No escala a cero: se duerme, con arranque en frío de 10-20 s. |
| **Container Apps** (Consumption) | 180.000 vCPU-seg + 360.000 GiB-seg + 2M requests **por suscripción, por mes** | Exige imagen de contenedor y un registry.                                                                                        |
| **Azure Functions**              | 1M ejecuciones                                                                | SignalR y una API con estado no encajan en el modelo.                                                                            |

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

| Componente     | Recurso                                                  |
| -------------- | -------------------------------------------------------- |
| PWA            | Static Web Apps, tier Free                               |
| API            | Container Apps, perfil **Consumption**, `minReplicas: 0` |
| Imagen         | GitHub Container Registry (`ghcr.io`)                    |
| Base de datos  | Azure SQL, free offer, serverless con auto-pausa         |
| Tiempo real    | SignalR in-process, **sin** Azure SignalR Service        |
| Observabilidad | Application Insights (5 GB/mes gratis)                   |

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

## Adenda del 2026-09-15: las fotos de la carta

US-06 sumó un recurso que no estaba en la tabla: una **cuenta de Azure Storage** con un
container de blobs (`product-images`) para las fotos de los productos. Es el único componente
**fuera de los tiers gratuitos**: Blob Storage no tiene oferta gratuita permanente. A los
volúmenes de un boliche —decenas de fotos, unos pocos MB— cuesta centavos por mes, y el límite
de gasto de la suscripción sigue siendo la garantía real.

Cómo tiene que crearse, cuando exista `infra/`:

- SKU **Standard LRS**, el más barato; no hay nada que replicar geográficamente.
- **Lectura pública a nivel de blob** (`allowBlobPublicAccess: true` en la cuenta, acceso
  `Blob` en el container). La API guarda la URL del blob y el celular del cliente la carga
  directo en un `<img>`, sin token ni pasar por la API. El container lo crea la propia API
  en la primera subida.
- La cadena de conexión llega a la API como `ImageStorage__ConnectionString`. En desarrollo
  es `UseDevelopmentStorage=true` contra Azurite, desde `docker-compose.yml`.

Se descartó guardar las fotos en la base o en el disco del contenedor: la base gratuita tiene
32 GB pero cada foto pasaría por la API en cada carta que se abre, y el disco de Container
Apps se pierde en cada reinicio.

## Adenda del 2026-09-27: el PWA le habla a la API directo, con CORS

El primer deploy mostró que la tabla de arriba tenía un hueco: en desarrollo el proxy de
Angular reenvía `/api` a la API, pero en Azure nada cumple ese rol. La Static Web App en el
plan **Free** no puede reenviar a un backend propio (los "linked backends" son del plan
Standard, ~USD 9/mes), y `staticwebapp.config.json` no hace proxy a URLs externas. El
`POST /api/…` llegaba a la Static Web App y respondía `405`.

Decisión: el PWA llama a la API en su propio host y la API habilita **CORS** para el host
del PWA. Nada de eso queda escrito en el código:

- La URL de la API es un output del Bicep (`apiFqdn`). El workflow compila el PWA después
  de desplegar la infraestructura y se la pasa con `ng build --define`. Un interceptor la
  antepone a cada `/api/…`; en desarrollo queda vacía y el proxy sigue como estaba.
- Los orígenes permitidos los arma el Bicep: el hostname de la Static Web App, más el dominio
  propio cuando exista (variable `FRONTEND_CUSTOM_DOMAIN` del environment). Llegan a la API
  como `Cors__AllowedOrigins__N`.

Costo: el build del PWA espera al de la infraestructura, unos minutos más por deploy. El
token viaja en `Authorization` y no en una cookie, así que no hace falta el modo con
credenciales de CORS.

## Adenda del 2026-09-27: la API manda telemetría a Application Insights

El recurso `appi-drinkit` existía desde el primer deploy y el Bicep ya le pasaba su cadena de
conexión al Container App, pero la API no tenía nada que la leyera: el recurso estaba vacío.
Lo único visible eran los logs de consola que Container Apps manda a Log Analytics.

Se suma `Azure.Monitor.OpenTelemetry.AspNetCore` (MIT), la distribución de OpenTelemetry que
Microsoft recomienda en lugar del SDK clásico de Application Insights. Registra requests,
excepciones, logs y las llamadas a SQL y a Blob Storage. Se activa sólo si está
`ApplicationInsights:ConnectionString`: en desarrollo no hay recurso, y la distribución
falla al arrancar sin cadena de conexión.

Sigue dentro del tier gratuito: los primeros 5 GB de ingesta por mes no se cobran, y el
tráfico de un boliche queda muy por debajo. Si algún día se acerca, se baja con el
`SamplingRatio` de la distribución.

## Qué no pudimos verificar

Los límites citados salen de la documentación oficial de Microsoft a la fecha de este ADR.
Las ofertas gratuitas de Azure cambian: conviene reverificarlas antes de crear los recursos,
y no asumir que siguen vigentes dentro de seis meses.
