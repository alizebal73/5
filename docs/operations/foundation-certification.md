# Foundation Certification

Foundation is not certified by file existence or green unit tests alone.

Required evidence before the first business vertical slice:
- clean Windows restore/build/test;
- architecture/source-size/placeholder/business guards;
- native Desktop launch and typed Server boundary;
- real PostgreSQL migration and concurrency lane;
- durable Audit, Idempotency and Outbox persistence;
- authenticated Agent identity, credential, connection lease, heartbeat, reconnect and fencing proof;
- Desktop -> Server real smoke;
- backup and isolated restore smoke;
- exact commit evidence recorded in the stable-checkpoint document.

Installer, updater and rollback are release gates, not prerequisites for starting business modules. They remain mandatory before a production release.

Until these runtime gates are green, business implementation remains blocked.
