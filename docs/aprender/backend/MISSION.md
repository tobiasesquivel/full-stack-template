# Mission: Entender el backend de full-stack-template

## Why
Tobi programa con agentes: ellos escriben el código y él decide y revisa. Hoy no entiende buena parte del backend, entonces no puede pedir bien un cambio ni detectar cuándo un agente se equivoca.

## Success looks like
- Le pide a un agente una query o un command con las piezas correctas, sin que el agente tenga que adivinar.
- Revisa el cambio de un agente en una slice y encuentra lo que falta: el paso por el bus, el registro, el codegen, el permiso o la traducción de errores.
- Explica con sus palabras cada carpeta de `Users`: `Access`, `Domain`, `MessageNames`, `OrchestrateAccountClosure`, `ScheduleAccountClosure`.
- Explica, sobre el cierre de cuenta, qué es una saga, para qué está RabbitMQ y por qué hay dos hosts.

## Constraints
- Solo backend. Español simple, desde el código real del repo.
- De lo chico (una slice) a lo grande (procesos y mensajes).
- Sin ejercicios de código: practica con quizzes.

## Out of scope
- Escribir el código él mismo.
- Frontend.
- `Infrastructure/` por dentro: se usa como caja negra hasta que una lección la necesite.
