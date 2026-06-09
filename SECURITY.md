# Security Policy

## Reporting Vulnerabilities

If you discover a security vulnerability in Atelier, please report it responsibly by emailing **jeff.conradt@infalligence.com** with the subject line `[SECURITY] Atelier Vulnerability Report`. Reports are received by the Atelier maintainer on behalf of the copyright holder, Infalligence Labs LLC.

Include:
- A clear description of the vulnerability.
- Steps to reproduce (if applicable).
- The affected version(s).
- Any workaround or mitigation you have identified.

Please do not open a public issue or pull request for an unpatched security vulnerability. Reports are acknowledged within 48 hours, with an estimated timeline for a fix.

## Supported Versions

Atelier is early-stage software under active development. Only the latest release receives security updates:

| Version | .NET Target | Status      | Security Updates |
|---------|-------------|-------------|------------------|
| 0.1.x   | .NET 10     | Current     | Yes              |
| < 0.1.0 | .NET 10     | Pre-release | No               |

Upgrade to the latest version as soon as patches are available.

## Security Considerations

### Source Generation

Atelier's dependency-injection scaffolding is generated at compile time rather than resolved through runtime reflection. Wiring is visible in the generated source and checkable by the analyzers, which keeps the composition surface auditable.

### Result Types

The `Outcome` / `Outcome<T>` contracts make error paths explicit in the type system. Unhandled failure branches are visible to static analysis and tests rather than surfacing as silent exceptions.

### Facilities

Facilities (caching, messaging, identity, and others) are pluggable and isolated. Review third-party or custom facility implementations against your own security requirements before deployment.

### Data Protection and Regulatory Scope

Atelier is a domain-agnostic infrastructure framework. It provides no data-subject-rights machinery (consent capture, access/erasure/export, retention-by-record) and no data-residency or region-pinning controls. The identity and audit layers capture principal and tenant identifiers for operational purposes only; they do not implement erasure or export pathways for those identifiers.

When Atelier is deployed into a context governed by GDPR, CCPA, or similar regulation, the deploying party owns all data-subject-rights and data-residency obligations for personal data flowing through the system. Do not assume the audit or identity layers satisfy erasure, consent, or residency requirements.

## Disclaimer

Atelier is provided "as is" without warranty of any kind. No software is without risk; you are responsible for assessing Atelier's suitability for security-sensitive workloads and for integrating it securely into your systems.
