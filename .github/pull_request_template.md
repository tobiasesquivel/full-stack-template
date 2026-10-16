# Descripción

## Alcance atómico

- [ ] Un solo cambio observable (contrato lleva backend, front, tests y spec juntos)

## Evidencia

- [ ] `dotnet build TemplateApp.slnx --configuration Release`
- [ ] `dotnet run --project dune/Dune.CommentVerifier -- --root .`
- [ ] `dotnet test backend/Tests/TemplateApp.Tests.csproj --configuration Release`
- [ ] `dotnet test dune/Dune.Analyzers.Tests/Dune.Analyzers.Tests.csproj --configuration Release`
- [ ] `node dune/check-structure.mjs` y `node --test dune/check-structure.test.mjs`
- [ ] `npm ci` y `npm run build` en `frontend/`, `node --test ../dune/check-frontend.test.mjs` y `node ../dune/check-frontend.mjs --root ..`
- [ ] `npm ci --prefix frontend`, `node --test dune/check-comments.test.mjs` y `node dune/check-comments.mjs --root .`
- [ ] `docker compose config`
- [ ] `node .agents/skills/verify-template/scripts/smoke-api.mjs --compose` con `API_BASE_URL` terminada en `/` y `OTEL_EXPORT_DIR` exportado

## Contratos afectados

## Riesgos
