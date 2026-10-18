# Authentication Lifecycle

## Operator

Operator authentication is Server-authoritative.

Required controls:

- password hashing;
- failed-attempt handling;
- lockout/throttling;
- explicit session/token expiry;
- logout/revocation policy;
- password change;
- password reset;
- privileged-action permission checks;
- audit.

## Bootstrap

The first operator is created through an explicit first-run setup flow.

There is no embedded default credential.

## Secrets

JWT signing keys and machine credentials are deployment secrets.

They are generated/provided during setup, stored outside source control, redacted from logs, and rotatable.

## Customer identity

Customer identifiers and customer credentials are separate concepts.

A customer does not become a different identity because a device reconnects or a Session token changes.

## Tests

Required:

- valid login;
- invalid credential;
- lockout;
- password change;
- token expiry;
- revoked session;
- missing permission;
- first-run bootstrap closes after successful setup.
