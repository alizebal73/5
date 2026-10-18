# Database Change Policy

Every schema change is a reviewed migration artifact.

Production Server startup never silently changes schema. Deployment owns migration execution. Clean-database and upgrade-from-previous-version tests are required.

Destructive changes require compatibility sequencing and explicit approval. Backfills must be restartable and observable.
