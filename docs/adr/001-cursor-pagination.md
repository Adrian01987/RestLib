# ADR-001: Cursor-Based Pagination Over Offset

**Status:** Accepted  
**Date:** 2026-01-25

## Context

REST APIs need pagination for list endpoints. The two common approaches are:

- **Offset-based:** `?page=3&limit=20` or `?offset=40&limit=20`
- **Cursor-based:** `?cursor=eyJpZCI6MTAwfQ&limit=20`

We need to choose a default pagination strategy for RestLib's `GetAll` endpoint.

## Options Considered

| Option       | Pros                                                    | Cons                                                                             |
| ------------ | ------------------------------------------------------- | -------------------------------------------------------------------------------- |
| Offset-based | Simple to understand, familiar, allows "jump to page N" | O(n) performance degradation, inconsistent on inserts/deletes, exposes internals |
| Cursor-based | O(1) performance, stable during mutations, opaque       | Slightly more complex, no "jump to page N" capability                            |

## Decision

Use **cursor-based pagination** as the default.

## Rationale

1. **Zalando Rule 160** recommends cursor-based pagination for its stability and performance characteristics
2. **Performance:** Offset pagination degrades linearly as the offset increases (`OFFSET 10000` is slow in most databases)
3. **Consistency:** With offset pagination, inserting or deleting records between requests causes items to be skipped or duplicated
4. **Opacity:** Cursors hide implementation details — the cursor could encode an ID, timestamp, or composite key without clients knowing
5. **Industry adoption:** Major APIs (Slack, Stripe, GitHub GraphQL) use cursor-based pagination

## Consequences

- **No "page number" concept** — clients must traverse sequentially using `next` links
- **Cursors must be URL-safe** — we use base64url encoding
- **Repository interface must support cursor-based queries** — the `PaginationRequest` includes a `Cursor` property
- **Response includes navigation links** — `self`, `first`, `next`, `prev` for client convenience
- **`prev` is structurally present but always null** — simple cursor pagination is forward-only; the `prev` property exists on the response model for future bidirectional cursor support but is not populated in the current implementation

## Security Considerations

Cursors are base64url-encoded JSON containing the pagination key value (e.g.,
`{"v":"<entity-id>"}`). They are **opaque by convention but not by enforcement**:

- **Not signed or encrypted.** Clients can decode a cursor, modify the key value, and
  re-encode it to resume pagination from an arbitrary position. This is generally
  acceptable because the same data is accessible via other endpoints (GetById) and
  cursors do not grant access beyond what the client's authorization already permits.
- **Do not embed sensitive data.** Because cursors are trivially reversible, entity key
  values used as cursor payloads must not contain secrets, tokens, or personally
  identifiable information that is not already exposed through the API.
- **Length-limited.** A configurable `MaxCursorLength` (default: 4096 characters) is
  enforced before any decoding attempt. Cursors exceeding this limit are rejected with
  a 400 Problem Details response, preventing large-payload abuse.
- **Decode failures are safe.** Malformed or tampered cursors that fail base64 decoding
  or JSON deserialization result in a 400 response. No partial state is applied.

If a future version requires tamper-proof cursors (e.g., for access-controlled
pagination windows), the encoding can be extended with HMAC signatures without
changing the external cursor format — clients already treat cursors as opaque strings.

## References

- [Zalando RESTful API Guidelines - Rule 160](https://opensource.zalando.com/restful-api-guidelines/#160)
- [Slack API Pagination](https://api.slack.com/docs/pagination)
- [Use The Index, Luke - Pagination Done the Right Way](https://use-the-index-luke.com/no-offset)
