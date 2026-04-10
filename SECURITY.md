# Security Policy

## Supported Versions

| Version | Supported          |
|---------|--------------------|
| 1.x     | Yes                |
| < 1.0   | No                 |

## Reporting a Vulnerability

If you discover a security vulnerability in RestLib, please report it responsibly
through [GitHub's private vulnerability reporting](https://github.com/Adrian01987/RestLib/security/advisories/new).

**Please do not open a public issue for security vulnerabilities.**

### What to include

- A description of the vulnerability and its potential impact.
- Steps to reproduce the issue, or a minimal proof of concept.
- The version(s) of RestLib affected.

### What to expect

- **Acknowledgement** within 48 hours of your report.
- **Status update** within 7 days with an initial assessment.
- **Fix timeline** communicated once the issue is confirmed. Critical vulnerabilities
  will be prioritised for the next patch release.

You will be credited in the release notes unless you prefer to remain anonymous.

## Security Considerations

RestLib handles HTTP request parsing, input validation, and response serialization.
The following areas are security-relevant:

- **Cursor pagination**: Cursors are base64url-encoded JSON. They are opaque by
  convention but not signed or encrypted. Do not embed sensitive data in cursor
  payloads. A maximum cursor length is enforced to prevent abuse.
- **Filtering and sorting**: Query parameter values are validated and type-checked
  before use. Unknown parameters are silently ignored.
- **Patch operations**: Patch documents are validated against the entity model and
  data annotations before persistence.
- **Batch operations**: Batch size limits are enforced to prevent resource exhaustion.
- **Error responses**: RFC 9457 Problem Details are used for all error responses.
  Internal exception details are never exposed to clients.
