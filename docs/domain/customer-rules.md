# Customer Rules

Customer owns business identity/profile.

Customer does not own:

- wallet balance;
- debt balance;
- inventory;
- Session occupancy;
- Station ownership.

Those are separate authorities.

## Identity

- Customer code is unique within the site.
- Customer code is normalized uppercase.
- Customer profile changes are versioned.
- Optional PIN is stored only as a salted password hash.
- PIN is never returned by API.
- Deactivation is explicit and audited.

## Future ownership

Wallet balance/debt -> Wallet module.
Session history/ownership -> Sessions module.
VIP entitlement -> VIP module.
