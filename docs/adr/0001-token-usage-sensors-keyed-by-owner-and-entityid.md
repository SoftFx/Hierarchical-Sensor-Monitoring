# Token usage sensors are keyed by owner login + token EntityId, not token names

Token names are not unique (a user may mint two tokens named "ci") and are
mutable (rename exists), while the public TokenId must never appear in
management responses (anti-enumeration) — so a token's self-monitoring subtree
is keyed by `<owner-login>/<entityId>`: collision-free and stable across
renames, at the cost of readability. To bridge that cost, the Profile token
card displays the EntityId, making the tree path ↔ token correlation a glance.
Rejected: name-based paths (collide, move on rename, orphaning history) and
TokenId-based paths (leaks the authentication lookup key to anyone with sight
on the self-monitoring product).
