# ADR-0005: Gateway delante y un proyecto .NET por proceso

- Estado: aceptada
- Fecha: 2026-11-18
- Decide: Tobi
- Cambia: el punto 7 de [ADR-0004](0004-vertical-slices-deep-modules.md) ("Un solo proyecto .NET")

## Contexto

El paso 23 del [plan](../evolucion/plan.md) extrae una feature a su propio proceso, con un gateway adelante. ADR-0004 dice "Un solo proyecto .NET", y `architecture.md` dice que el backend es un solo proyecto. Las dos cosas no entran juntas: un proceso propio necesita su propio proyecto.

La investigación está en [paso-23-servicio-gateway.md](../evolucion/investigacion/paso-23-servicio-gateway.md). Recomienda el patrón Strangler Fig (migrar de a partes detrás de una fachada): primero el gateway delante del monolito, después la extracción de una feature ([Azure Architecture Center, Strangler Fig](https://learn.microsoft.com/en-us/azure/architecture/patterns/strangler-fig)).

## Decisión

1. **Un proyecto .NET por proceso.** El monolito (`backend/TemplateApp.csproj`) sigue siendo un proyecto. Cada proceso nuevo tiene el suyo. Dentro de cada proyecto, Dune hace cumplir los límites, como dice ADR-0004.
2. **El gateway es un proceso propio, sin código de features.** Vive en `gateway/`, usa YARP (`Yarp.ReverseProxy` 2.3.0, MIT) y solo reparte requests por URL. No referencia otro proyecto. Toda request a la API entra por él: ningún proceso de la API publica puertos en Compose, y nginx reenvía `/api/` solo al gateway. `check-structure.mjs` lo hace cumplir (regla "gateway como única entrada").
3. **Cada proceso tiene su propio health.** El ready del gateway mira solo al gateway. A cada destino lo vigila el chequeo activo de YARP: con el destino caído, su ruta responde 503 y el gateway sigue listo.
4. **Dos pasos.** El paso 23 pone el gateway delante del monolito sin cambiar el backend. El paso 23.2 extrae `Tasks` a su propio proceso detrás del gateway, con una librería compartida para `Infrastructure/` y `Shared/`.

## Consecuencias

- Los analyzers de Dune corren también en `TemplateApp.Gateway` (`dune/Dune.props`).
- Hay un salto de red más. El gateway reemplaza el `X-Forwarded-For` que manda nginx: la API sigue viendo la IP de un proxy, igual que antes (pendiente de forwarded headers en `progreso.md`).
- El gateway tiene su propia telemetría (`templateapp-gateway`): una traza cruza los dos procesos.
- Hasta el paso 23.2, todo el código de features sigue en un solo ensamblado y `internal` no separa una feature de otra (`conventions.md`).
- Paso 23.2: la librería compartida es `backend/Platform/TemplateApp.Platform.csproj` y el host de Tasks es `backend/Hosts/Tasks/TemplateApp.Tasks.csproj`. Ningún `.cs` cambió de carpeta. La librería también lleva `AppDbContext` y sus migraciones viejas: los dos procesos corren el arranque de una base heredada, bajo el mismo lock ([investigación](../evolucion/investigacion/paso-23-2-extraer-tasks.md), sección 6).
