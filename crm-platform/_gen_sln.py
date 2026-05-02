#!/usr/bin/env python3

import uuid

def new_guid():
    return str(uuid.uuid4()).upper()

projects = [
    'src/services/identity-service/CrmPlatform.IdentityService.csproj',
    'src/services/identity-service/Tests/CrmPlatform.IdentityService.Tests.csproj',
    'src/services/platform-admin-service/CrmPlatform.PlatformAdminService.csproj',
    'src/services/platform-admin-service/Tests/CrmPlatform.PlatformAdminService.Tests.csproj',
    'src/services/sfa-service/CrmPlatform.SfaService.csproj',
    'src/services/sfa-service/Tests/CrmPlatform.SfaService.Tests.csproj',
    'src/services/css-service/CrmPlatform.CssService.csproj',
    'src/services/css-service/Tests/CrmPlatform.CssService.Tests.csproj',
    'src/services/marketing-service/CrmPlatform.MarketingService.csproj',
    'src/services/marketing-service/Tests/CrmPlatform.MarketingService.Tests.csproj',
    'src/services/analytics-service/CrmPlatform.AnalyticsService.csproj',
    'src/services/analytics-service/Tests/CrmPlatform.AnalyticsService.Tests.csproj',
    'src/services/notification-service/CrmPlatform.NotificationService.csproj',
    'src/services/notification-service/Tests/CrmPlatform.NotificationService.Tests.csproj',
    'src/services/integration-service/CrmPlatform.IntegrationService.csproj',
    'src/services/integration-service/Tests/CrmPlatform.IntegrationService.Tests.csproj',
    'src/services/ai-orchestration-service/CrmPlatform.AiOrchestrationService.csproj',
    'src/services/ai-orchestration-service/Tests/CrmPlatform.AiOrchestrationService.Tests.csproj',
    'src/functions/journey-orchestrator/CrmPlatform.Functions.JourneyOrchestrator.csproj',
    'src/functions/lead-score-decay/CrmPlatform.Functions.LeadScoreDecay.csproj',
    'src/functions/sla-orchestrator/CrmPlatform.Functions.SlaOrchestrator.csproj',
    'src/services/_template/CrmPlatform.ServiceTemplate.csproj',
    'src/services/_local/auth-stub/CrmPlatform.AuthStub.csproj',
]

guids = [(path, new_guid()) for path in projects]
CS_PROJECT_TYPE = '{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}'

lines = [
    '',
    'Microsoft Visual Studio Solution File, Format Version 12.00',
    '# Visual Studio Version 17',
    'VisualStudioVersion = 17.0.31903.59',
    'MinimumVisualStudioVersion = 10.0.40219.1',
]

for path, guid in guids:
    name = path.split('/')[-1].replace('.csproj', '')
    lines.append(f'Project("{CS_PROJECT_TYPE}") = "{name}", "{path}", "{{{guid}}}"')
    lines.append('EndProject')

lines += [
    'Global',
    '\tGlobalSection(SolutionConfigurationPlatforms) = preSolution',
    '\t\tDebug|Any CPU = Debug|Any CPU',
    '\t\tRelease|Any CPU = Release|Any CPU',
    '\tEndGlobalSection',
    '\tGlobalSection(ProjectConfigurationPlatforms) = postSolution',
]

for path, guid in guids:
    lines.append(f'\t\t{{{guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU')
    lines.append(f'\t\t{{{guid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU')
    lines.append(f'\t\t{{{guid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU')
    lines.append(f'\t\t{{{guid}}}.Release|Any CPU.Build.0 = Release|Any CPU')

lines += [
    '\tEndGlobalSection',
    '\tGlobalSection(SolutionProperties) = preSolution',
    '\t\tHideSolutionNode = FALSE',
    '\tEndGlobalSection',
    'EndGlobal',
    '',
]

print('\n'.join(lines))
