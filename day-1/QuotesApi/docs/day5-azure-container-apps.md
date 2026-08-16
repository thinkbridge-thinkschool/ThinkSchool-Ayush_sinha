# Day 5 — Azure Container Apps Fundamentals

## Objective

This exercise demonstrates provisioning and verifying an Azure Container
Apps environment for the QuotesApi containerized application: creating the
supporting resource group, creating the Container Apps environment, and
confirming both with actual Azure CLI output. It also confirms that the
application's existing .NET container image configuration is in a state
that could be published to a container image, without performing a live
deployment.

## Azure subscription

- Subscription name: Azure for Students
- Subscription ID: `708f56eb-d40f-4658-adde-d6f5866dad34`
- Tenant ID: `8d46a076-d093-416d-a57b-8692cde13bf8`
- State: `Enabled`

Obtained from:

```bash
az account show \
  --query '{subscription:name,subscriptionId:id,tenantId:tenantId,state:state}' \
  -o json
```

## Resource group

- Name: `thinkschool-rg`
- Location: `centralindia`
- Provisioning state: `Succeeded`
- Resource ID: `/subscriptions/708f56eb-d40f-4658-adde-d6f5866dad34/resourceGroups/thinkschool-rg`

The resource group did not exist prior to this exercise and was created
with:

```bash
az group create \
  --name thinkschool-rg \
  --location centralindia
```

## Container Apps environment

- Name: `thinkschool-env`
- Resource group: `thinkschool-rg`
- Location: `Central India`
- Provisioning state: `Succeeded`
- Resource ID: `/subscriptions/708f56eb-d40f-4658-adde-d6f5866dad34/resourceGroups/thinkschool-rg/providers/Microsoft.App/managedEnvironments/thinkschool-env`

The Container Apps environment did not exist prior to this exercise and
was created with:

```bash
az containerapp env create \
  --name thinkschool-env \
  --resource-group thinkschool-rg \
  --location centralindia
```

Creating the environment also auto-generated a Log Analytics workspace
(no workspace was supplied explicitly), since `appLogsConfiguration` was
left as the `log-analytics` default.

## Verification

```bash
az containerapp env show \
  --name thinkschool-env \
  --resource-group thinkschool-rg \
  --output json
```

Actual result (abridged to the fields relevant to this exercise):

```json
{
  "name": "thinkschool-env",
  "resourceGroup": "thinkschool-rg",
  "location": "Central India",
  "provisioningState": "Succeeded",
  "id": "/subscriptions/708f56eb-d40f-4658-adde-d6f5866dad34/resourceGroups/thinkschool-rg/providers/Microsoft.App/managedEnvironments/thinkschool-env"
}
```

```bash
az containerapp env list \
  --resource-group thinkschool-rg \
  --output table
```

Actual result:

```
Location       Name             ResourceGroup
-------------  ---------------  ---------------
Central India  thinkschool-env  thinkschool-rg
```

## Application container configuration

QuotesApi already carries .NET SDK container publishing configuration in
`QuotesApi.csproj`:

- Image name: `quotes-api`
- Tag: `0.1.0`
- Base image: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`
- Container Runtime Identifier: `linux-musl-x64` (required because the
  base image is musl-libc/Alpine; without it, publish would embed the
  glibc build of the SQLite native library, which fails to load at
  runtime inside the container)
- Target framework: `net10.0`

Container publishing is provided entirely by .NET SDK container tooling
(`Microsoft.NET.Build.Containers`, driven by the `Container*` MSBuild
properties above). No Dockerfile is used, and none exists in the
repository.

This configuration was not modified as part of this exercise — it was
already in place and reviewed for correctness. No image was built, and no
image was pushed to any registry (Azure Container Registry or otherwise)
as part of this exercise.

## Container Apps fundamentals

### Resource group
A logical Azure resource container that groups related resources for
lifecycle and access management.

### Container Apps environment
A shared, managed environment for Container Apps that provides the
underlying networking, Log Analytics workspace, and infrastructure that
one or more Container Apps run inside.

### Container App
A deployable application resource that runs a container image inside a
Container Apps environment.

### Revisions
Immutable versions of a Container App's configuration and image, used to
manage deployments and control traffic between versions.

### Ingress
Controls external or internal HTTP access and routing to a Container
App, including the target port the container listens on.

### Scaling
Container Apps can scale the number of running replicas automatically
based on configured scale rules (e.g. HTTP concurrency, CPU, or
KEDA-based custom triggers).

### Secrets/configuration
Runtime secrets should be supplied through Azure configuration/secrets
mechanisms (e.g. Container Apps secrets, Key Vault references) rather
than committed into source control.

## Deployment status

No live Container App deployment was performed because the verified
exercise requirements for this task were resource-group and Container
Apps environment provisioning/verification; no repository/task evidence
required deploying QuotesApi as a live Container App.

No image was pushed to Azure Container Registry or any other registry.
No Container App exists in `thinkschool-rg`. There is no live application
URL, and `/health` has not been verified against any deployed instance.

## Result

The required Azure Container Apps environment (`thinkschool-env`) and its
supporting resource group (`thinkschool-rg`) were provisioned and
verified successfully via actual Azure CLI output. QuotesApi's existing
.NET container image configuration was reviewed and confirmed correct.
No live application deployment was performed or required.
