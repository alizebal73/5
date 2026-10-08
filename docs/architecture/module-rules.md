# Module Rules

Domain -> Shared
Application -> Domain + Shared + Server.Application ports
Server.Application ports -> Shared
Infrastructure adapters -> Application + Domain + Persistence + Server.Application + Shared
Api -> Application + Shared
Composition -> registration only

EF/DbContext is forbidden inside src/Server/Modules. Persistence adapters live under Server.Infrastructure.Persistence and may implement module repository ports.

Forbidden: Infrastructure from Domain/Application/Api; cross-module internal references; network/file/process side effects in Domain; authoritative calculations in clients.
