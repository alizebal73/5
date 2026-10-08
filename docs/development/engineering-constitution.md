# Engineering Constitution

1. Requirement -> acceptance -> ownership/invariants -> contract/ADR -> implementation -> regression -> build -> runtime evidence -> release evidence.
2. Foundation and Business are separate stages.
3. Main contains only reviewed/certified work.
4. Every change has one clear reason and a rollback path.
5. Production defects create permanent regression evidence.
6. Shared contracts are the single DTO/request/response authority.
7. Client UI never owns authoritative calculations.
8. Production startup never silently migrates the database.
9. Composition files stay small.
10. No fake-green certification.
