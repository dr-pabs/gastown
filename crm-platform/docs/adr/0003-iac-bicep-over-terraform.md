# ADR 0003 — IaC: Bicep over Terraform

**Status**: Accepted
**Date**: 2026-03-29
**Deciders**: Technical Director, DevOps Engineer

## Context

Infrastructure as Code tooling choice for an Azure-only platform.

## Decision

Use **Azure Bicep** rather than Terraform or Pulumi.

## Reasons

- Azure-native — Bicep compiles to ARM templates, no abstraction layer over the Azure API
- No state file to manage or secure (unlike Terraform's remote state)
- First-class support for all Azure resource types on day of release
- Simpler syntax than ARM JSON, no HCL to learn
- Native GitHub Actions support via `azure/arm-deploy`
- PSRule.Rules.Azure provides Bicep-aware linting and Well-Architected Framework checks

## Consequences

**Positive**: Simpler toolchain, no state management overhead, always up to date with Azure API.
**Negative**: Not portable to other cloud providers (acceptable — platform is Azure-only by design).
