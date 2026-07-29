# sarvam-org-service v2 — Integration Playbook

Audience: frontend + product-backend teams consuming `/api/v2/*`.
Companion to: `docs/sarvam-org-service-api-v2-openapi.yaml`.

---

## 1. TL;DR

1. Sign user in via Kratos (existing flow). You get a **Kratos session** — token (native API) or cookie (browser/OIDC).
2. Trade the session for a **v2 JWT**: `POST /api/v2/auth/jwt`. Empty body `{}` is fine — the service auto-picks the caller's default org + workspace.
3. Use the JWT as `Authorization: Bearer <jwt>` on every other `/api/v2/*` call.
4. Fetch `/api/v2/.well-known/jwks.json` once on your server, cache, validate JWTs locally.
5. Errors come back as `{ error, detail, owner, org_id? }` with a stable error code.

**Browser one-liner** (cookie auto-sent by browser):
```js
const { token } = await (await fetch('/api/v2/auth/jwt', {
  method: 'POST', credentials: 'include',
  headers: {'Content-Type': 'application/json'}, body: '{}',
})).json();
```

That's the whole contract. Everything below is detail.

---

## 2. Authentication

Every `/api/v2/*` endpoint falls into one of three buckets. The OpenAPI spec lists the requirement per operation; this section explains *why*.

### 2.1 JWT bearer (default, 22 of 25 operations)

Send `Authorization: Bearer <jwt>` on every call. The token is minted by `POST /api/v2/auth/jwt` and **always** carries this exact triple:

| Claim | Meaning |
|---|---|
| `sub_id` | Kratos identity that owns the session |
| `org_id` | Org the token is scoped to |
| `workspace_id` | Workspace the token is scoped to — every JWT has one, never absent |
| `email` | OIDC-standard email claim, snapshotted from Kratos `identity.traits.email` at mint time. Stale within the token TTL if the user updates their email — re-mint to refresh. Use for display / convenience; fetch `GET /api/v2/me` for the live value. |
| `exp` | 15 minutes after mint |
| `iss` | `V2_JWT_ISSUER` env value (e.g. `https://auth.azure-qa01.sarvam.ai`) |
| `aud` | `V2_JWT_AUDIENCE` env value — defaults to `sarvam-internal`. Verifiers MUST check this (RFC 7519 §4.1.3). |
| `jti` | Per-mint UUID. Reserved for a future server-side blocklist; clients can ignore. |

The org+workspace context inside the token is **advisory**. The server re-checks membership and grant state against Postgres + Keto on every request, so a stale or forged claim doesn't grant access. Practical implication: you don't need to re-mint when role/grants change — the next gate check sees the new state.

### 2.2 Kratos session (`POST /api/v2/auth/jwt` only)

The mint endpoint accepts **either** auth surface — header (native flows) **or** cookie (browser flows). The body is fully optional.

**Header form (native API / non-browser callers):**
```
POST /api/v2/auth/jwt
Content-Type: application/json
X-Session-Token: <kratos-session-token>

{ "org_id"?: "<uuid>", "workspace_id"?: "<uuid>" }
```
The `X-Session-Token` value is what Kratos returns as `session_token` from the API password/code login flow (or any other API flow that yields one).

**Cookie form (browser / OIDC):**
```
POST /api/v2/auth/jwt
Content-Type: application/json
Cookie: sarvam_identity_session=<value>   ← sent automatically by the browser

{ "org_id"?: "<uuid>", "workspace_id"?: "<uuid>" }
```
The OIDC browser flow doesn't emit a separate session_token — only the cookie. The cookie's `domain=.azure-qa01.sarvam.ai` / `SameSite=None; Secure` means it's attached on any cross-subdomain request with `credentials: 'include'`.

**Auto-pick semantics (both forms):**
- `org_id` omitted → caller's default org (the one seeded at signup, ordered `is_default DESC`).
- `workspace_id` omitted → Owner gets the org's default workspace; Member gets the first workspace they hold a grant on (no grants ⇒ `403 WORKSPACE_ACCESS_DENIED`).
- Either explicitly passed → service uses that and validates membership/grant.
- Both omitted → first-login UX: `POST {}` is enough.

**Operational notes:**
- Kratos is hit exactly once, here, at mint time. Subsequent JWT-gated calls are stateless against Kratos.
- **Rate limit:** 10/minute keyed on the Kratos session token (sha256). Burst returns `429`.
- If both header and cookie are present, the header wins.

**Response body:**
```json
{
  "token": "<jwt>",
  "expires_at": "2026-06-01T12:30:00+05:30",
  "sub_id": "<uuid>",
  "org_id": "<uuid>",
  "workspace_id": "<uuid>",
  "email": "user@example.com"
}
```
The `email` field mirrors the JWT's `email` claim — surfaced on the response body so the FE doesn't have to decode the token to display the caller. Same staleness contract as the claim.

### 2.3 Public (no auth)

Only two endpoints:

- `GET /api/v2/.well-known/jwks.json` — public JWKS for signature verification.
- `POST /api/v2/invitations/accept` — single-use. The body carries only `invitation_id`; the Kratos session is presented via the `X-Session-Token` header (native) or the `sarvam_identity_session` cookie (OIDC browser flow), exactly like `POST /api/v2/auth/jwt`. **Rate-limited 10/min per session.**

---

## 3. Verifying JWTs (server-side integrators)

Pseudocode for a product backend that consumes v2 JWTs:

```python
import httpx, jwt, time

JWKS_URL = "https://<host>/api/v2/.well-known/jwks.json"
ISSUER   = "https://<host>"
AUDIENCE = "sarvam-internal"    # V2_JWT_AUDIENCE on the deployed service

_jwks_cache = None
_jwks_fetched_at = 0

def get_jwks():
    global _jwks_cache, _jwks_fetched_at
    if _jwks_cache is None or time.time() - _jwks_fetched_at > 3600:
        _jwks_cache = httpx.get(JWKS_URL).json()
        _jwks_fetched_at = time.time()
    return _jwks_cache

def verify_v2_jwt(token: str) -> dict:
    headers = jwt.get_unverified_header(token)
    kid = headers["kid"]
    key = next(k for k in get_jwks()["keys"] if k["kid"] == kid)
    return jwt.decode(
        token, jwt.algorithms.ECAlgorithm.from_jwk(key),
        algorithms=["ES256"], issuer=ISSUER, audience=AUDIENCE,
        options={"verify_exp": True, "verify_aud": True},
    )
```

Key points:

- **Algorithm:** ES256 (NIST P-256 ECDSA). Reject anything else. JWK shape: `kty=EC, crv=P-256, alg=ES256`.
- **`kid`:** the JWKS will rotate. Re-fetch (don't crash) when an incoming token's `kid` is unknown. Current QA kid is `key-2026-05`; the format is `key-YYYY-MM` so a deployed rotation will surface as a new kid string.
- **JWKS path:** **must** be `/api/v2/.well-known/jwks.json`. The unscoped `/.well-known/jwks.json` is owned by Hydra and will return Hydra's keys — those are NOT valid for v2 tokens.
- **`iss`:** verify, don't accept arbitrary issuers. Source of truth is the `V2_JWT_ISSUER` env on the deployed service (we'll publish per-env values separately).
- **`aud`:** verify (RFC 7519 §4.1.3). Source of truth is the `V2_JWT_AUDIENCE` env on the service — defaults to `sarvam-internal`. Reject tokens missing or with a mismatched `aud`.

### 3.1 Sample JWKS response

What `GET /api/v2/.well-known/jwks.json` actually returns:

```json
{
  "keys": [
    {
      "kty": "EC",
      "crv": "P-256",
      "x": "<base64url, ~43 chars>",
      "y": "<base64url, ~43 chars>",
      "kid": "key-2026-05",
      "use": "sig",
      "alg": "ES256"
    }
  ]
}
```

The two crypto-material fields are `x` and `y` — the affine coordinates of the P-256 public point. Most JWT libraries (`PyJWT`'s `ECAlgorithm.from_jwk`, `jose`, `node-jose`, etc.) take this JWK dict directly and produce the verification key. There's no need to reconstruct the EC key by hand.

Token shape (header + payload + signature):
- Header: `{"alg":"ES256","typ":"JWT","kid":"key-2026-05"}`
- Signature: 64 raw bytes → ~96 base64url chars (about a third the size of the RS256 signatures the service used to mint).
- A typical full token lands at ~600 bytes — well within any header / cookie limit.

---

## 4. Error envelope

Every 4xx/5xx response has this shape:

```json
{
  "error":   "INVITATION_NOT_ACTIVE",
  "detail":  "invitation is revoked — cannot be accepted",
  "owner":   "CUSTOMER",
  "org_id":  "019e5e..."
}
```

| Field | Notes |
|---|---|
| `error` | Stable string code. Use this for branching logic. |
| `detail` | Human-readable. Safe to surface to end users for `owner: CUSTOMER` errors. |
| `owner` | `CUSTOMER` = client should act (fix input / re-auth / etc.); `SARVAM` = server-side problem, surface generic message. |
| `org_id` | Set when the error pertains to a specific org. |

Common codes you'll see (full list in the spec):

| Code | HTTP | When |
|---|---|---|
| `ACTIVE_INVITATION_ALREADY_EXISTS` | 409 | Re-inviting an email that already has an active invite for the same org |
| `ALL_INVITE_WORKSPACES_STALE` | 409 | Every workspace on the invitation has been soft-deleted |
| `ALREADY_ORG_MEMBER` | 409 | Inviting someone who's already a member |
| `GRANT_ALREADY_EXISTS` | 409 | POST grant for a (workspace, sub) that's already active — use PATCH to change role |
| `GRANT_TARGET_NOT_ORG_MEMBER` | 404 | Granting workspace access to a sub who's not in the parent org — invite them first |
| `INVITATION_EMAIL_MISMATCH` | 403 | Session email doesn't match the invitation's email |
| `INVITATION_NOT_ACTIVE` | 409 | Invitation is revoked or already consumed |
| `KRATOS_SESSION_INVALID` | 401 | Session expired / revoked while minting JWT |
| `NOT_AN_ORG_MEMBER` | 403 | Caller has a valid JWT but isn't a member of the requested org |
| `RATE_LIMIT_EXCEEDED` | 429 | `/auth/jwt` or `/invitations/accept` window burst |
| `WHITELIST_ENTRY_FROZEN` | 409 | Tried to remove from org product whitelist — API is append-only |

---

## 5. Common flows

### 5.1 First-time signup (server-driven)

1. Frontend completes Kratos registration → Kratos webhooks `POST /internal/api/v2/hooks/kratos/register` synchronously.
2. Hook seeds the new identity's **default org + default workspace** atomically, bound to all currently-ACTIVE products. Names are personalised from the registrant's first name: `"<First>'s Organisation"` and `"<First>'s Workspace"` (falls back to the email local-part if Kratos hasn't populated a name yet). The user can rename either via `PATCH` and the override sticks — no client-side fallback needed.
3. Frontend immediately calls `POST /api/v2/auth/jwt` with `credentials: 'include'` (cookie attached) and an empty body — service auto-picks the default org + workspace. No need to call `/me/orgs` first.
4. From here on, use the bearer JWT on every `/api/v2/*` call.

Idempotency: replaying the hook for the same `sub_id` returns the existing default org/workspace; no duplicates created and names are NOT re-derived (a rename survives a re-run).

### 5.2 Switching org

1. `GET /api/v2/me/orgs` → list of orgs the caller belongs to (bounded by per-identity cap of 150; uses the same `?limit=` + `?cursor=` pagination as the other list endpoints — see §7). Each row carries enough to render an org-switcher *and* the General Settings header without a second round-trip:
   ```jsonc
   [
     {
       "org_id":          "019e5ef5-2563-7113-a78c-8ae61dfb1e2f",
       "name":            "Sarvam AI",
       "is_default":      true,
       "role":            "owner",          // caller's org role
       "created_by":      "a1b2c3d4-…",      // sub_id of creator
       "created_by_name": "Mani",            // from identity mirror — null if not synced
       "created_at":      "2025-05-20T10:00:00Z"
     }
   ]
   ```
2. `POST /api/v2/auth/jwt` with the new `org_id` (and the same Kratos session, via header or cookie) → fresh 15-min JWT scoped to that org **and a concrete workspace within it** (auto-picked if you don't pass `workspace_id` — see §2.2).
3. Replace the cached bearer; existing tokens for the old org keep working until they expire (no global invalidation).

`GET /api/v2/orgs/{org_id}` returns the full Org shape with the same `created_by` / `created_by_name` / `created_at` triple plus the editable metadata fields (`gst_number`, `tan_number`, `address`, `org_logo_url`). `created_by_name` is `null` if the creator's identity mirror hasn't synced a display name yet.

### 5.3 Inviting a member to an org + workspaces

1. **Owner** calls `POST /api/v2/orgs/{org_id}/invitations`:
   ```json
   {
     "email": "newuser@example.com",
     "role": "member",
     "workspace_grants": [
       {"workspace_id": "<uuid>", "role": "editor"},
       {"workspace_id": "<uuid>", "role": "admin"}
     ]
   }
   ```
   - `workspace_grants` is **required** and **non-empty** (≥1).
   - One active invite per `(org_id, email)` — re-inviting before revoking returns `409 ACTIVE_INVITATION_ALREADY_EXISTS`.

2. Send the resulting `invitation_id` to the invitee out-of-band (email link / etc.). The invitation itself does NOT email — caller is responsible for delivery.

   **In-app fallback if the email is lost/spam:** the invitee can list their pending invitations with `GET /api/v2/me/invitations` (JWT-authed) and accept from inside the app. See §5.6.

3. **Invitee** signs up (or signs in) via Kratos and calls:
   ```
   POST /api/v2/invitations/accept
   Content-Type: application/json
   X-Session-Token: <kratos-session-token>     ← native flow
   # — or, browser/OIDC: omit the header and let the
   #   `sarvam_identity_session` cookie ride along (credentials: 'include')

   { "invitation_id": "<uuid>" }
   ```
   - Public endpoint (no bearer). The Kratos session comes from the `X-Session-Token` header **or** the `sarvam_identity_session` cookie (same contract as `POST /api/v2/auth/jwt`); send one or the other. Neither present → `401`.
   - Session email must match the invitation email or you get `403 INVITATION_EMAIL_MISMATCH`.
   - Single-use; second accept returns `409 INVITATION_NOT_ACTIVE`.
   - Response: `{ org_id, seated_workspace_ids, skipped_stale_grants }`. Workspaces soft-deleted between invite and accept are **silently skipped** in `skipped_stale_grants`. If *every* workspace is gone, the whole accept fails with `409 ALL_INVITE_WORKSPACES_STALE` and membership is rolled back.

4. To revoke before acceptance: `DELETE /api/v2/orgs/{org_id}/invitations/{invitation_id}` → `204`. Idempotent: revoking an already-revoked invitation returns `409 INVITATION_NOT_ACTIVE`, not 204 — surface it to the caller as "already revoked".

### 5.4 Workspace grants — add / promote / remove

- **Add:** `POST /api/v2/workspaces/{workspace_id}/grants` with `{sub_id, role}`. Requires the sub to already be an org member; otherwise `404 GRANT_TARGET_NOT_ORG_MEMBER`.
- **Change role:** `PATCH .../grants/{sub_id}` with `{role}`. Returns 200 + the row.
- **Remove:** `DELETE .../grants/{sub_id}` → `204`.
- **Re-grant after remove**: the POST returns 201 — soft-deleted grants are reactivated atomically, no special "undelete" call needed.

### 5.5 Org product whitelist

- `GET /api/v2/orgs/{org_id}/products` → list of product IDs (empty = no restriction).
- `PATCH .../products` with `{add_products: [...]}` (append-only).
- Removal is **not exposed via API** — `WHITELIST_ENTRY_FROZEN` is returned if you try. Removal goes through Sarvam Support out-of-band.

### 5.6 Invitee-side: listing pending invitations

```
GET /api/v2/me/invitations
Authorization: Bearer <jwt>
```

Returns active invitations addressed to the caller's email (resolved from the JWT `sub_id` via the identity mirror — the caller doesn't pass an email):

```json
[
  {
    "invitation_id": "<uuid>",
    "org_id": "<uuid>",
    "org_name": "Sarvam org",
    "role": "member",
    "workspace_grants": [
      { "workspace_id": "<uuid>", "workspace_name": "Default Workspace", "role": "editor" }
    ],
    "invited_by_email": "owner@acme.com",
    "created_at": "2026-05-27T10:00:00Z"
  }
]
```

- Use this on app open to render a "you have N pending invites" banner — no need to depend on the invite email reaching the user.
- Acceptance still goes through `POST /api/v2/invitations/accept` with the `invitation_id` (§5.3).
- Soft-deleted workspaces in the invite are filtered out of `workspace_grants` (matches the accept-time staleness rule).
- No pagination — invitations per user are bounded and small.

### 5.7 Listing members with their workspace grants

`GET /api/v2/orgs/{org_id}/members` returns one row per active member **and** per pending invitee (UNION), each carrying its `workspace_grants` so the FE can render a "Workspaces" column without a second round-trip.

**Filtering by status:** the FE settings page has separate "Active members" and "Pending invitations" tabs. Pass `?status=active` or `?status=invited` to scope the response to just that arm — pagination metadata (`total`, `next_cursor`) is then computed over that arm alone, so each tab paginates independently. Omit the param for the combined feed (default). `?status=` combines with `?q=` and `?role=` (search/filter within the selected arm). An unknown value returns `400`.

```
GET /api/v2/orgs/{id}/members                              # combined (default)
GET /api/v2/orgs/{id}/members?status=active                # only seated members
GET /api/v2/orgs/{id}/members?status=invited               # only pending invites
GET /api/v2/orgs/{id}/members?status=active&q=avi          # search within active
```

Combined-feed response shape (per row):

```json
{
  "rows": [
    {
      "sub_id": "<uuid>",
      "email": "avi@sarvam.ai",
      "name": "Avi",
      "role": "member",
      "status": "active",
      "created_at": "...",
      "invitation_id": null,
      "workspace_grants": [
        { "workspace_id": "<uuid>", "workspace_name": "Default Workspace", "role": "editor" },
        { "workspace_id": "<uuid>", "workspace_name": "Analytics", "role": "admin" }
      ]
    },
    {
      "sub_id": null,
      "email": "pending@example.com",
      "role": "member",
      "status": "invited",
      "invitation_id": "<uuid>",
      "workspace_grants": [
        { "workspace_id": "<uuid>", "workspace_name": "Default Workspace", "role": "editor" }
      ]
    }
  ],
  "pagination": { "next_cursor": null, "has_more": false, "total": 2 }
}
```

- For `status: "active"` — grants come from the workspace_grants table for that sub in this org.
- For `status: "invited"` — grants come from the pending invitation's workspace_grants.
- Stale (soft-deleted) workspaces are filtered out of both. Empty `workspace_grants` is normal for a member with no per-workspace grants (e.g. an Owner — they have implicit admin everywhere and no explicit rows).
- Two batch queries, regardless of page size — no N+1.

---

## 6. Rate limits

| Endpoint | Limit | Key | What happens on burst |
|---|---|---|---|
| `POST /api/v2/auth/jwt` | 10 / minute | sha256 of `X-Session-Token` (no header → shared `no-session` bucket) | `429 RATE_LIMIT_EXCEEDED` |
| `POST /api/v2/invitations/accept` | 10 / minute | sha256 of the session credential — `X-Session-Token` header **or** `sarvam_identity_session` cookie (no creds → shared `no-session` bucket) | `429 RATE_LIMIT_EXCEEDED` |

No endpoint buckets by client IP — IPs aren't reliable behind an LB, so requests with no auth credential collectively share one `no-session` bucket (they're rejected by the auth guard anyway). Per-session callers get their own bucket regardless of which auth surface (header or cookie) they present. Limits are tunable per env via `V2_JWT_MINT_RATE_LIMIT` and `V2_INVITATION_ACCEPT_RATE_LIMIT`.

All other endpoints are unlimited at the service layer. If you're seeing 429s elsewhere, it's coming from upstream infra (ingress / WAF), not us.

Mitigation: cache the JWT for its full 15-minute lifetime — don't re-mint per request.

---

## 7. Pagination

List endpoints take `limit` (default 25, max 100) and `cursor` (opaque round-trip string).

```
GET /api/v2/orgs/{id}/members?limit=25
{ "rows": [...], "pagination": { "next_cursor": "...", "has_more": true, "total": 42 } }
```

- `cursor` is opaque — pass it back verbatim. Don't try to decode.
- Garbage cursor → `400 INVALID_CURSOR`.
- `limit > 100` → `400` (we run all framework validation errors through a unified 400 handler).
- `GET /api/v2/me/orgs` accepts the same params; the per-identity cap is 150 orgs, so with the default `limit=25` an aggressive user can span multiple pages — paginate just like the other list endpoints.

---

## 8. ID conventions

- Service-owned ids (`org_id`, `workspace_id`, `invitation_id`) are **UUIDv7** (chronologically sortable): `019e5ef5-2563-7113-a78c-8ae61dfb1e2f` — version digit is `7`.
- `sub_id` is **UUIDv4** (issued by Kratos, not by us — version digit `4`).
- All UUID variants are distinct id-spaces; you don't need to disambiguate between them when storing or routing.
- Soft-delete is invisible at the API: deleted rows return `404`. There's no `?include_deleted=true` knob.

---

## 9. Sanity check: spec ↔ live API

We ship `tests/smoke/v2_spec_conformance.py` which loads the OpenAPI spec and drives every declared operation against the running service. Run it post-deploy as a smoke gate:

```
poetry run python3 tests/smoke/v2_spec_conformance.py
```

Expected output ends with `PASS=48  FAIL=0`. Any mismatch (status code outside the declared response set, auth gate misbehaving) shows up as a `FAIL` line with the offending request/response.

This isn't just docs theatre — every fix we ship that touches the API has to keep this script green. If your integration trips a code path that's not in the spec, please send us the response body and we'll either fix the backend or the spec.

---

## 10. Reference URLs

| Resource | Path |
|---|---|
| OpenAPI spec (this repo) | `docs/sarvam-org-service-api-v2-openapi.yaml` |
| Conformance test | `tests/smoke/v2_spec_conformance.py` |
| Swagger UI (drag-drop) | https://editor.swagger.io |
| Redoc local preview | `npx @redocly/cli preview-docs docs/sarvam-org-service-api-v2-openapi.yaml` |

---

## 11. Internal API (service-to-service)

`/internal/api/v2/*` is a separate surface from the FE-facing `/api/v2/*`. It exists for **migration scripts** and **internal services** that need to act on behalf of users without a JWT — the caller is a service, so the actor is passed explicitly in the request body / query (`owner_sub`, `created_by`, `updated_by`, `sub_id`).

- **Network reachability is the primary boundary** — `/internal/api/v2/*` is hidden from the public OpenAPI spec and only routable from inside the cluster (LB doesn't expose it). The Kratos registration webhook lives on this surface for the same reason.
- **Shared-secret guard** — every route requires `X-Internal-Token: <V2_INTERNAL_API_TOKEN>` (same header on every call; sourced from Key Vault in QA/prod). Missing or wrong → `401`. The Kratos webhook itself sends this header per its `kratos.yaml` `auth.config` block.
- **No JWT** — the caller is a service. There is no `sub_id`/`org_id`/`workspace_id` derived from a bearer; pass the actor in the body.
- **Validation** — same v2 contract as the public surface: `extra='forbid'`, RequestValidationError → `400`, domain exceptions → 4xx via the unified error envelope (see §4).

### 11.1 Migration-support lookup

```
GET /internal/api/v2/identities/by-email?email=<addr>
X-Internal-Token: <secret>

→ 200
{
  "sub_id":           "<uuid>",
  "email":            "user@example.com",
  "name":             "User Name",
  "default_org": {
    "org_id":         "<uuid>",
    "name":           "User's Organisation"
  },
  "default_workspace": {
    "workspace_id":   "<uuid>",
    "name":           "User's Workspace"
  }
}
```

- **Email is case-insensitive** — the identity mirror stores lower-cased; the lookup lowercases on the way in.
- **`404 IDENTITY_NOT_FOUND`** — email isn't in the mirror. Migration is expected to follow signup, not precede it; provision the user through Kratos first, then call this endpoint.
- **`404 ORG_NOT_FOUND` / `WORKSPACE_NOT_FOUND`** — invariant-violation path (every signup seeds a default org + workspace). Surfaces explicitly rather than 500ing so the migration script can flag the row for operator attention.

### 11.2 Migration writes (seat users into orgs/workspaces)

These thin endpoints layer over the same services as the public API but take the actor from the body:

| Endpoint | Body actor | Notes |
|---|---|---|
| `POST /internal/api/v2/orgs` | `owner_sub` (seated as Owner) | Idempotency: caller dedupes by listing first. |
| `GET  /internal/api/v2/orgs?owner_sub=<uuid>` | — | Migration preflight: list this owner's orgs before POSTing. |
| `POST /internal/api/v2/orgs/{id}/workspaces` | `created_by` | Idempotent on default workspace; named workspaces append. |
| `GET  /internal/api/v2/orgs/{id}/workspaces` | — | Enumerate; supports `?page=`+`?page_size=` like the public surface. |
| `PATCH /internal/api/v2/workspaces/{id}` | `updated_by` | Rename + product-binding adds. |
| `POST /internal/api/v2/orgs/{id}/members` | `sub_id`, `created_by` | Idempotent seat. FK-violates if `sub_id` isn't a real identity → `404 IDENTITY_NOT_FOUND`. |
| `PATCH /internal/api/v2/orgs/{id}/members/{sub_id}` | `updated_by` | Role update (member ⇄ owner). Last-owner protection applies. |
| `POST /internal/api/v2/workspaces/{id}/grants` | `sub_id`, `created_by` | Sub must already be an org member; otherwise `404`. Dup grant → `409`. |
| `GET  /internal/api/v2/workspaces/{id}/access/{sub_id}` | — | Single PDP decision (owner ⇒ ADMIN; grant ⇒ role; stranger ⇒ deny). |
| `POST /internal/api/v2/access/check` | — | Batch PDP; up to 100 `(workspace_id, sub_id)` checks per call. |

**Per-identity / per-org caps (defaults; env-overridable as `V2_MAX_ORGS_PER_IDENTITY` / `V2_MAX_WORKSPACES_PER_ORG`):**
- `max_orgs_per_identity`: **150** (hits return `409 ORG_CAP_REACHED`)
- `max_workspaces_per_org`: **150** (hits return `409 WORKSPACE_CAP_REACHED`)

The expected migration shape is: (1) signup user via Kratos → (2) `GET /identities/by-email` to anchor → (3) `POST /orgs/{id}/members` (or `/workspaces/{id}/grants`) to seat them. The composite lookup is one round-trip; the seat-writes are idempotent so re-runs are safe.

---

## 12. Operations notes

Three things ops needs to know that aren't visible from the API surface:

### 12.1 Postgres session-level defaults

Run these once per environment (dev/QA/prod) against the org-service database. They scope `lock_timeout` / `statement_timeout` / `idle_in_transaction_session_timeout` at the **database** level, so every new app connection inherits them at session start — no per-tx round-trip, and code paths that bypass `db_tx()` still get the protection.

```sql
ALTER DATABASE <db> SET lock_timeout                          = '5s';
ALTER DATABASE <db> SET idle_in_transaction_session_timeout   = '60s';
ALTER DATABASE <db> SET statement_timeout                     = '30s';
```

- `lock_timeout` — a waiter on a row lock fails fast instead of hanging.
- `statement_timeout` — a runaway query (planner regression, full scan) dies in 30s rather than parking a connection until socket teardown.
- `idle_in_transaction_session_timeout` — a backend orphaned by a crashed client self-terminates and releases its locks.

The migration runner opts OUT for itself via `SET <name> = 0` in `migrations/env.py`, so long backfills aren't killed by `statement_timeout=30s`.

### 12.2 Postgres connection pool

The SQLAlchemy `AsyncEngine` is built via `service_base.PostgresAsyncRepository.create()` which honors these `DBSettings` defaults:

| Setting | Default | Env override | Notes |
|---|---|---|---|
| `database_pool_size` | 5 | `DATABASE_POOL_SIZE` | "Steady-state" pool — persistent connections kept open. |
| `database_pool_max_overflow` | 2 | `DATABASE_POOL_MAX_OVERFLOW` | Burst capacity over `pool_size`; closed when idle. |
| `database_pool_timeout` | 30s | `DATABASE_POOL_TIMEOUT` | How long a request waits for a free conn before erroring. |
| `database_pool_recycle` | 1800s | `DATABASE_POOL_RECYCLE` | Max age of a pooled connection (defends against NAT/firewall stales). |

**The unconfigured ceiling is 7 connections per pod** (5 + 2). For QA, **`DATABASE_POOL_SIZE=20` + `DATABASE_POOL_MAX_OVERFLOW=10` (cap 30/pod)** is a reasonable starting point and matches typical FastAPI service sizing; for prod, size against the Postgres `max_connections` budget divided by replica count, with headroom for migrations and ops connections.

### 12.3 HTTPX outbound retry policy

Every outbound HTTP call from the v2 surface goes through the shared `httpx.AsyncClient` wrapped with `RetryTransport` (see `core/http/retry_transport.py`). Tunable via env:

| Setting | Default | Meaning |
|---|---|---|
| `V2_HTTP_RETRY_MAX_ATTEMPTS` | 3 | Total attempts (including the first). `1` disables retries. |
| `V2_HTTP_RETRY_MAX_WAIT_SECONDS` | 10.0 | Cap on a single backoff sleep (incl. `Retry-After`). |
| `V2_HTTP_RETRY_BACKOFF_BASE_SECONDS` | 0.5 | Base of exp-backoff: 0.5/1/2/4/… + full-jitter. |

**What gets retried:**
- Transport errors (`ConnectError`, `NetworkError`, `TimeoutException`) — wire failed, server may not have seen it.
- HTTP `500`, `502`, `503`, `504`, `408`, `429` — transient / overload / hint.

**What doesn't:**
- 4xx other than 408/429 — caller bug, retrying won't fix.
- POST / PATCH by default (RFC 7231 non-idempotent). Callers that know an endpoint is idempotent opt in by setting `X-Retry-Safe: 1` on the request; the transport strips the header before sending.

`Retry-After: <seconds>` on 429/503 is honored (clamped to `MAX_WAIT_SECONDS`).

---

## 13. Quick contact

- Bug / spec mismatch / surprise behaviour → file in the org-service repo's issues, tag the on-call.
- Auth questions, JWT validation snippets for other languages → ping #org-service-api.
- Production env URLs (host, `V2_JWT_ISSUER` values per env) → see internal infra docs; not pinned here because they change.
