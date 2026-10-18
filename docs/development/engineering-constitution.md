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

11. Every slice has a complete Ready record before implementation; only explicitly documented critical-incident containment is exempt from the normal sequence.
12. Done is evidence-based and SHA-specific; old green checks never certify new code.
13. New bugs use docs/templates/bug-fix-template.md; historical entries in the Bug-Fix Log are append-only.
14. Every bug fix has a regression test or a reviewed written exception explaining why automated testing is infeasible.
15. Product slices use docs/templates/vertical-slice-template.md and satisfy Definition of Ready and Definition of Done.
16. Requirements use stable REQ IDs; unresolved money, data ownership, authorization, concurrency or recovery decisions block ordinary implementation.
