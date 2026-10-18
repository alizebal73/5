# Module Rules

Domain -> Shared
Application -> Domain + Shared + explicit contracts
Infrastructure -> Application + Domain + Persistence + Shared
Api -> Application + Shared
Composition -> registration only

Forbidden: EF/DbContext in Domain/Application/Api; Infrastructure from Domain; cross-module internal references; network/file/process side effects in Domain; authoritative calculations in clients.
