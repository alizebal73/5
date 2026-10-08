# Threat Model

Trust boundaries:
Operator Desktop -> Server API
Agent -> Server realtime/API
Server -> PostgreSQL
Server -> filesystem
Update package -> installed components

Rules:
- IP address is never identity;
- credentials are rotatable/revocable;
- authentication and authorization are separate;
- financial/security-sensitive mutations are audited;
- secrets never enter source control;
- update packages require integrity verification.
