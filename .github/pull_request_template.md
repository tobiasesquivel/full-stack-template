# Descripción

## Alcance atómico

- [ ] Un solo cambio observable (contrato lleva backend, front, tests y spec juntos)

## Evidencia

- [ ] `dotnet build TemplateApp.slnx --configuration Release`
- [ ] `dotnet test backend/Tests/TemplateApp.Tests.csproj --configuration Release`
- [ ] `dotnet test tools/Dune.Analyzers.Tests/Dune.Analyzers.Tests.csproj --configuration Release`
- [ ] `node tools/check-structure.mjs` y `node --test tools/check-structure.test.mjs`
- [ ] `npm ci` y `npm run build` en `frontend/`
- [ ] `docker compose config`
- [ ] `node .agents/skills/verify-template/scripts/smoke-api.mjs --compose` con `API_BASE_URL` terminada en `/` y `OTEL_EXPORT_DIR` exportado

## Contratos afectados

## Riesgos
