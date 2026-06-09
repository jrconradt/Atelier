# Diagnostics

`Atelier.Framework.Analyzers` enforces the framework's composition rules at compile time. Each rule has an `ATELIER*` (or legacy `ATE*`) identifier. The tables below list the rules with their titles and default severities as defined in the analyzer source.

Suppress a rule the same way as any Roslyn diagnostic — `#pragma warning disable ATELIERxxxx`, a `[SuppressMessage]` attribute, or a `<NoWarn>` entry — but prefer fixing the underlying pattern; most of these are `Error` severity precisely because they guard the programming model.

## Architecture and instantiation

| ID | Severity | Title |
|---|---|---|
| ATELIER001 | Error | ServiceProvider access detected |
| ATELIER002 | Warning | ServiceProvider property access detected |
| ATE1001 | Error | No manual instantiation of lifecycle-managed types |
| ATE1002 | Error | No manual instantiation of contract types |
| ATE1003 | Warning | Pooled instance not returned |
| ATE1004 | Warning | No manual instantiation of service types |

## Dependency injection and requisites

| ID | Severity | Title |
|---|---|---|
| ATELIER0600 | Error | Missing DI registration for requisite dependency |
| ATELIER0601 | Error | Missing DI registration for constructor dependency |
| ATELIER0602 | Info | Consider adding `[Infrastructure]` attribute |
| ATELIER0603 | Warning | Cross-assembly requisite dependency may not be auto-discovered |
| ATELIER0604 | Warning | `ActivatorUtilities.CreateInstance` bypasses auto-discovery |
| ATELIER0605 | Info | Assembly reference required for auto-discovery |
| ATELIER1402 | Warning | Requisite dependency target missing `[Infrastructure]` attribute |

## Operations and parameter validation

| ID | Severity | Title |
|---|---|---|
| ATELIER0010 | Error | Missing null guard on `[Operation]` non-nullable reference parameter |
| ATELIER003 | Warning | Missing null check in `[Operation]` method |
| ATELIER004 | Warning | Method parameters must be validated |
| ATELIER1310 | Error | Missing `CancellationToken` guard in `[Operation]` method |
| ATELIER1404 | Warning | Public service method missing `[Operation]` attribute |

## Outcome pattern

| ID | Severity | Title |
|---|---|---|
| ATELIER1000 | Error | Operation throws exception instead of returning `Outcome.Failure()` |
| ATELIER1001 | Warning | Service method throws exception instead of returning `Outcome.Failure()` |
| ATELIER1002 | Info | `ArgumentException` in operation — consider validation before the operation |

## Async discipline

| ID | Severity | Title |
|---|---|---|
| ATELIER1200 | Warning | Missing `ConfigureAwait(false)` in library code |
| ATELIER1201 | Warning | `ConfigureAwait(true)` in library code — should be false |
| ATELIER1300 | Error | Synchronous blocking on async operation using `.Result` |
| ATELIER1301 | Error | Synchronous blocking on async operation using `.Wait()` |
| ATELIER1302 | Error | Synchronous blocking on multiple tasks using `Task.WaitAll`/`WaitAny` |

## Network and security

| ID | Severity | Title |
|---|---|---|
| ATELIER0300 | Warning | Missing Network Zone |
| ATELIER0310 | Error | Network Policy Violation |
| ATELIER0320 | Warning | Unencrypted Service Communication |
| ATELIER0330 | Warning | Undeclared service dependency |
| ATELIER0710 | Error | API method has no authorization or anonymous opt-out |
| ATELIER0720 | Error | Authenticated facility method would bypass AuthorizeAsync |
| ATELIER0730 | Error | Authorization metadata is declared where nothing enforces it |
| ATELIER0740 | Error | Secret-bearing contract member must be marked [JsonIgnore] |
| ATELIER0741 | Error | Authorization claim or scope is not a catalog constant |
| ATELIER0750 | Error | Mutating API operation has no write-tier scope |
| ATELIER0751 | Error | Operation name is lexically ambiguous between read and mutation |
| ATELIER0752 | Error | [ScopeResource] target does not expose both READ and WRITE scope constants |

## Lifetime and state

| ID | Severity | Title |
|---|---|---|
| ATELIER0400 | Warning | Singleton service has mutable state |
| ATELIER0401 | Info | Scoped service without state |
| ATELIER0402 | Warning | Repository should be scoped |
| ATELIER0403 | Error | Dispose method on a type that declares neither IDisposable nor IAsyncDisposable |

## Context

| ID | Severity | Title |
|---|---|---|
| ATELIER1100 | Error | IContext parameter in local service - use ambient context instead |
| ATELIER1101 | Error | IContext parameter in non-Facility interface |

## Contract placement and versioning

| ID | Severity | Title |
|---|---|---|
| ATELIER0200 | Warning | DTO class should be marked `[Contract]` |
| ATELIER0210 | Error | Contract version change must declare backward compatibility |
| ATELIER1500 | Error | [Contract] attribute on interface - should only be on DTOs |
| ATELIER1501 | Error | [Contract] attribute on abstract class - should only be on DTOs |
| ATELIER1502 | Error | [Contract] attribute on service class - should only be on DTOs |
| ATELIER1503 | Error | [Contract] attribute on behavior class - should only be on DTOs |

## Code generation and composition

| ID | Severity | Title |
|---|---|---|
| ATELIER1600 | Warning | Redundant constructor in partial class with `[Requisite]` fields |
| ATELIER1610 | Error | `IAtelier`-implementing class must not declare a public constructor |

## Generator diagnostics

These identifiers are reported by source generators rather than the analyzer assembly, and fail the build. `ATELIER0700`/`ATELIER0701` come from `Atelier.Framework.Infrastructure.Generators`; `ATELIER0800`/`ATELIER0801` come from `Atelier.Framework.Network.Generators`. Each is defined as a `DiagnosticDescriptor` in its generator's source.

| ID | Severity | Title |
|---|---|---|
| ATELIER0700 | Error | Authenticated facility method must return Outcome |
| ATELIER0701 | Error | Domain gateway must specify source and target domains |
| ATELIER0800 | Error | Transport interface method must return Task or ValueTask |
| ATELIER0801 | Error | Transport interface method must declare at most one non-CancellationToken parameter |
