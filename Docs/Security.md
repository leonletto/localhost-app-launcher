# LHLauncher Security Considerations

## Document Information

| Item | Value |
|------|-------|
| Application | LHLauncher |
| Version | 1.0.x |
| Last Updated | December 2024 |
| Classification | Internal / Enterprise Security Review |

## Executive Summary

LHLauncher is a local helper utility that enables Workspace ONE Hub to launch locally installed applications via HTTPS URLs. The application implements defense-in-depth security controls including localhost-only network binding, strict certificate validation, input sanitization, and registry-based application allowlisting. This document details the security architecture, controls, and accepted risks for enterprise security review.

## Threat Model and Trust Boundaries

### Scope

LHLauncher operates as a user-mode application on Windows endpoints. It accepts HTTPS requests exclusively from the local loopback interface and executes pre-configured applications on behalf of the user.

### Trust Boundaries

1. **Browser to LHLauncher**: TLS-encrypted connection over loopback (127.0.0.1)
2. **LHLauncher to Windows Registry**: Read-only access to user-specific configuration (HKEY_CURRENT_USER)
3. **LHLauncher to Process Execution**: Spawns processes in the current user's security context

### Assets Protected

- Endpoint integrity (prevention of arbitrary code execution)
- User session (no privilege escalation)
- Network isolation (no remote access capability)

### Threat Actors Considered

| Actor | Capability | Mitigation |
|-------|------------|------------|
| Remote attacker | Network access to endpoint | Localhost-only binding; not reachable from network |
| Local unprivileged user | Access to local machine | Per-user registry isolation (HKCU) |
| Compromised browser | Arbitrary HTTP requests | Input validation, allowlist-only execution |
| Malicious website | Cross-origin requests | TLS certificate validation, localhost binding |

## Network Security

### Localhost-Only Binding

LHLauncher binds exclusively to the loopback address 127.0.0.1. This is hardcoded in the application and cannot be overridden by configuration. The binding occurs at application startup:

- IP Address: 127.0.0.1 (hardcoded)
- Default Port: 443 (configurable via registry, validated range 1-65535)
- Protocol: HTTPS/TLS only

This design ensures the service cannot accept connections from remote hosts, other machines on the network, or even other network interfaces on the same machine.

### HTTPS Requirement

Workspace ONE Access requires HTTPS URLs with valid, trusted certificates. The TLS layer provides:

- Transport integrity for the loopback connection
- Browser compatibility without security warnings
- Alignment with enterprise certificate trust policies

The certificate is not used to assert organizational identity to remote parties; it satisfies browser security requirements for a localhost service.

## Certificate Security

### Certificate Requirements

The application requires a certificate meeting the following criteria:

| Requirement | Validation |
|-------------|------------|
| Subject Name | Must match configured hostname (default: localhost.com) |
| Chain of Trust | Must chain to a trusted root CA |
| Not Self-Signed | Chain length must be greater than 1 |
| Validity Period | Current date must be within NotBefore and NotAfter |
| Key Usage | Must include Server Authentication EKU (OID 1.3.6.1.5.5.7.3.1) |
| Revocation Status | Certificate must not be revoked (CRL check performed) |

### Certificate Validation Controls

The application performs the following validation sequence when loading a certificate:

1. **Store Location Search**: Searches CurrentUser store first, then LocalMachine store
2. **Expiration Check**: Rejects certificates outside their validity period
3. **Chain Building**: Constructs and validates the certificate chain
4. **Revocation Check**: Performs online CRL verification (RevocationMode.Online)
5. **Self-Signed Rejection**: Rejects certificates with chain length of 1 or less
6. **EKU Validation**: Verifies Server Authentication extended key usage is present

Certificates failing any validation step are rejected and the failure is logged.

### Certificate Store Locations

The application searches for certificates in the following order:

1. Current User Personal Store (Cert:\CurrentUser\My)
2. Local Machine Personal Store (Cert:\LocalMachine\My)

### Hostname Configuration

The default hostname is localhost.com. A non-corporate hostname is used intentionally:

- Using a corporate domain would incorrectly imply a remote, centrally hosted service
- A localhost-scoped hostname makes the local nature of the service explicit
- Prevents phishing ambiguity with corporate web properties

The hostname resolves to 127.0.0.1 via the Windows hosts file, which is configured during deployment.

### Certificate Misuse Analysis

The certificate cannot be meaningfully exploited for the following reasons:

1. The service only binds to 127.0.0.1; external connections are impossible
2. The hostname resolves exclusively to loopback
3. Private key access requires certificate deployment privileges
4. An attacker with sufficient access to misuse the certificate would already have administrative control of the endpoint

This scenario represents a post-compromise situation outside the standard endpoint threat model.

## Input Validation

### HTTP Method Restriction

Only HTTP GET requests are processed. All other HTTP methods are rejected with HTTP 405 Method Not Allowed. This prevents:

- POST-based attacks
- PUT/DELETE operations
- Non-standard method exploitation

### Request Path Validation

All request paths are validated against a strict whitelist pattern before processing:

- Pattern: `^(/[\w\-\.]*|/?)$`
- Allows: Alphanumeric characters, hyphens, periods, and forward slash
- Blocks: Path traversal sequences, special characters, query strings, URL encoding exploits

Invalid paths are rejected with HTTP 400 Bad Request and logged for security monitoring.

### Request Size Limiting

Requests exceeding 2048 bytes are rejected with HTTP 413 Request Entity Too Large. This mitigates:

- Buffer overflow attempts
- Resource exhaustion attacks
- Malformed request exploitation

### Program Name Matching

Request paths are matched exactly against pre-configured program names. The matching process:

1. Extracts the path component from the HTTP request
2. Compares against the list of configured program names (case-sensitive)
3. Rejects requests that do not match any configured program

This ensures that only explicitly configured applications can be launched; arbitrary paths cannot trigger execution.

## Process Execution Security

### Registry-Based Allowlist

LHLauncher implements an allowlist model for process execution. Applications must be explicitly configured in the Windows Registry before they can be launched:

- Registry Location: HKEY_CURRENT_USER\SOFTWARE\LHLauncher
- Each application requires a subkey with:
  - Command: Full path to the executable with optional arguments
  - ProcessName: Process name used for verification

Applications not present in the registry cannot be executed regardless of the request path.

### Executable Path Validation

Before execution, the application validates:

1. The Command registry value is present and non-empty
2. The ProcessName registry value is present and non-empty
3. The executable file exists at the specified path

Invalid configurations are marked as not valid and will not execute.

### User Context Execution

All processes are spawned in the current user's security context:

- No privilege escalation occurs
- Processes inherit the user's permissions
- The application cannot execute programs the user could not run directly
- UAC prompts will appear for elevated operations (standard Windows behavior)

### Shortcut Resolution

The application supports Windows shortcut (.lnk) files:

- Shortcuts are resolved to their target executable
- Target path is validated for existence
- Arguments from the shortcut are preserved

## Configuration Security

### Registry Permissions

Configuration is stored under HKEY_CURRENT_USER, providing:

- Per-user isolation: Each user has independent configuration
- Standard user access: No administrative privileges required for configuration
- Write protection from other users: HKCU is writable only by the owning user and administrators

### Registry Values

| Value Name | Type | Purpose | Security Consideration |
|------------|------|---------|------------------------|
| hostName | String | Certificate subject name | Controls certificate selection |
| Port | String | Listening port | Validated to 1-65535 range |
| loggingPath | String | Log file location | User-writable path required |
| logLevel | String | Debug or Info | Controls logging verbosity |
| appCatalogURL | String | Redirect URL | Used in browser redirects |
| Custom Messages | String | Error/success messages | May contain HTML; see notes |

### Custom Message Security Note

The application allows HTML in custom messages (MisconfiguredAppMessage, FailedLaunchMessage, etc.). This content is rendered in the browser and originates from the user's own registry. If registry integrity is compromised, an attacker could inject misleading content. This is an accepted risk as registry compromise implies broader endpoint compromise.

### Hosts File Modification

Deployment includes modification of the Windows hosts file to resolve localhost.com to 127.0.0.1:

- Requires administrative privileges to modify
- Applied via deployment sensor (system context)
- Entry: 127.0.0.1 localhost.com

This ensures the hostname resolves correctly without depending on external DNS.

## Security Logging and Audit

### Logged Security Events

The application logs security-relevant events to support incident response and compliance:

| Event Category | Events Logged |
|----------------|---------------|
| Certificate Validation | Expiration failures, revocation detection, chain build failures, self-signed rejection, EKU validation failures |
| Request Processing | Invalid HTTP methods, invalid request paths, oversized requests |
| Process Execution | Command execution attempts, process start success/failure, retry attempts |
| Configuration | Registry read errors, missing configuration, invalid port values |

### Log Configuration

- Default Location: C:\ProgramData\LHLauncher\LHLauncher.log
- Configurable via: loggingPath registry value
- Log Levels: Info (default), Debug (verbose)
- Format: Timestamp [SourceFile:LineNumber] Message

### Security Monitoring Recommendations

For enterprise deployments, consider:

1. Forwarding logs to a SIEM for centralized monitoring
2. Alerting on certificate validation failures
3. Monitoring for unusual request patterns
4. Periodic review of configured applications

## Assumptions and Accepted Risks

### Security Assumptions

The following assumptions underpin the security model:

1. **Endpoint Integrity**: The Windows endpoint is not already compromised
2. **User Trust**: The logged-in user is authorized to run the configured applications
3. **Registry Integrity**: HKEY_CURRENT_USER has not been tampered with by malware
4. **Certificate Deployment**: Certificates are deployed through trusted enterprise mechanisms (Workspace ONE profiles)
5. **Network Isolation**: The endpoint firewall does not expose the listening port externally

### Accepted Risks

| Risk | Rationale for Acceptance |
|------|--------------------------|
| Registry tampering by user | Users can already run any application directly; registry access does not provide escalation |
| Certificate private key on endpoint | Required for TLS operation; protected by Windows certificate store ACLs |
| HTML in custom messages | Content originates from user's own registry; compromise implies broader endpoint compromise |
| Hosts file dependency | Managed centrally via deployment; required for hostname resolution |

### Out-of-Scope Threats

The following threats are explicitly out of scope:

1. **Post-compromise scenarios**: An attacker with administrative access can already execute arbitrary code
2. **Kernel-level rootkits**: Application-level controls cannot defend against kernel compromise
3. **Physical access attacks**: Physical access to the endpoint enables bypass of all software controls

## Deployment Security

### Deployment Components

| Component | Deployment Method | Security Consideration |
|-----------|-------------------|------------------------|
| LHLauncher.msi | Workspace ONE UEM | Signed MSI recommended |
| Root CA Certificate | Device Profile | Deployed to Computer store |
| Server Certificate | User Profile | Deployed to User Personal store |
| Registry Configuration | Sensor (PowerShell) | Runs in user context |
| Hosts File Entry | Sensor (PowerShell) | Runs in system context |

### Deployment Recommendations

1. Sign the MSI package with a code signing certificate
2. Use certificate profiles with appropriate key protection
3. Validate sensor scripts before deployment
4. Test in a pilot group before broad deployment
5. Monitor deployment success/failure via UEM console

## Compliance Considerations

### Control Mapping

| Control Area | Implementation |
|--------------|----------------|
| Access Control | Localhost-only binding, user-context execution |
| Input Validation | HTTP method restriction, path validation, size limits |
| Cryptographic Controls | TLS encryption, certificate validation, EKU enforcement |
| Audit and Logging | Security event logging, configurable verbosity |
| Configuration Management | Registry-based configuration, validation on load |

### Documentation for Auditors

When presenting to security auditors, emphasize:

1. The application cannot be reached from the network (127.0.0.1 binding)
2. Only pre-configured applications can be executed (allowlist model)
3. Certificate validation exceeds browser defaults (EKU, revocation, self-signed rejection)
4. All security-relevant events are logged
5. The application runs with standard user privileges (no elevation)

## Summary

LHLauncher implements a defense-in-depth security architecture appropriate for its role as a localhost helper utility. The primary security controls are:

1. **Network Isolation**: Hardcoded 127.0.0.1 binding prevents remote access
2. **Certificate Validation**: Multi-layer validation exceeding browser defaults
3. **Input Validation**: Strict filtering of HTTP methods, paths, and request sizes
4. **Allowlist Execution**: Only registry-configured applications can be launched
5. **User-Context Execution**: No privilege escalation capability
6. **Security Logging**: Comprehensive logging of security-relevant events

The design explicitly avoids self-signed certificates or local trust manipulation, ensuring full alignment with enterprise certificate policies while maintaining strict local-only access.
