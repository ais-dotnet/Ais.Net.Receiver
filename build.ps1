#Requires -Version 7
<#
.SYNOPSIS
    Runs a .NET flavoured build process.
.DESCRIPTION
    This script was scaffolded using a template from the Endjin.RecommendedPractices.Build PowerShell module.
    It uses the InvokeBuild module to orchestrate an opinonated software build process for .NET solutions.
.EXAMPLE
    PS C:\> ./build.ps1
    Downloads any missing module dependencies (Endjin.RecommendedPractices.Build & InvokeBuild) and executes
    the build process.
.PARAMETER Tasks
    Optionally override the default task executed as the entry-point of the build.
.PARAMETER ContainerRegistryType
    The type of container registry to use when publishing any images (supported values: acr,docker,ghcr)
.PARAMETER ContainerRegistryFqdn
    The fully-qualified domain name for the target container registry
.PARAMETER SourcesDir
    The path where the source code to be built is located, defaults to the current working directory.
.PARAMETER LogLevel
    The logging verbosity.
.PARAMETER BuildModulePath
    The path to import the Endjin.RecommendedPractices.Build module from. This is useful when
    testing pre-release versions of the Endjin.RecommendedPractices.Build that are not yet
    available in the PowerShell Gallery.
.PARAMETER BuildModuleVersion
    The version of the Endjin.RecommendedPractices.Build module to import. This is useful when
    testing pre-release versions of the Endjin.RecommendedPractices.Build that are not yet
    available in the PowerShell Gallery.
.PARAMETER InvokeBuildModuleVersion
    The version of the InvokeBuild module to be used.
#>
[CmdletBinding()]
param (
    [Parameter(Position=0)]
    [string[]] $Tasks = @("."),

    [Parameter()]
    [ValidateSet("", "docker", "acr", "ghcr")]
    [string] $ContainerRegistryType = "acr",

    [Parameter()]
    [string] $ContainerRegistryFqdn = "",

    [Parameter()]
    [string] $SourcesDir = $PWD,

    [Parameter()]
    [ValidateSet("minimal","normal","detailed")]
    [string] $LogLevel = "minimal",

    [Parameter()]
    [string] $Configuration = "Debug",

    [Parameter()]
    [string] $BuildModulePath,

    [Parameter()]
    [string] $BuildModuleVersion = "1.5.10-beta0003",

    [Parameter()]
    [version] $InvokeBuildModuleVersion = "5.10.3"
)

$ErrorActionPreference = $ErrorActionPreference ? $ErrorActionPreference : 'Stop'
$InformationPreference = 'Continue'

$here = Split-Path -Parent $PSCommandPath

#region InvokeBuild setup
# This handles calling the build engine when this file is run like a normal PowerShell script
# (i.e. avoids the need to have another script to setup the InvokeBuild environment and issue the 'Invoke-Build' command )
if ($MyInvocation.ScriptName -notlike '*Invoke-Build.ps1') {
    Install-PSResource InvokeBuild -Version $InvokeBuildModuleVersion -Scope CurrentUser -TrustRepository | Out-Null
    try {
        Invoke-Build $Tasks $MyInvocation.MyCommand.Path @PSBoundParameters
    }
    catch {
        if ($env:GITHUB_ACTIONS) {
            Write-Host ("::error file={0},line={1},col={2}::{3}" -f `
                            $_.InvocationInfo.ScriptName,
                            $_.InvocationInfo.ScriptLineNumber,
                            $_.InvocationInfo.OffsetInLine,
                            $_.Exception.Message
                        )
        }
        Write-Host -f Yellow "`n`n***`n*** Build Failure Summary - check previous logs for more details`n***"
        Write-Host -f Yellow $_.Exception.Message
        Write-Host -f Yellow $_.ScriptStackTrace
        exit 1
    }
    return
}
#endregion

#region Import shared tasks and initialise build framework
if (!($BuildModulePath)) {
    Install-PSResource Endjin.RecommendedPractices.Build -Version $BuildModuleVersion -Scope CurrentUser -TrustRepository | Out-Null
    $BuildModulePath = "Endjin.RecommendedPractices.Build"
}
else {
    Write-Information "BuildModulePath: $BuildModulePath"
}
Import-Module $BuildModulePath -RequiredVersion ($BuildModuleVersion -split '-')[0] -Force
Write-Host "Using Build module version: $((Get-Module Endjin.RecommendedPractices.Build | Select-Object -ExpandProperty Version).ToString())"

# Load the build process & tasks
. Endjin.RecommendedPractices.Build.tasks
#endregion

#
# Build process control options
#
$SkipInit = $false
$SkipVersion = $false
$SkipBuild = $false
$CleanBuild = $false
$SkipTest = $false
$SkipTestReport = $false
$SkipAnalysis = $false
$SkipPackage = $false
$SkipPublish = $false

#
# Build process configuration
#
$SolutionToBuild = (Resolve-Path (Join-Path $here "./Solutions/Ais.Net.Receiver.sln")).Path

# Synopsis: Build, Test and Package
task . FullBuild

# build extensibility tasks
task RunFirst {}
task PreInit {}
task PostInit {}
task PreVersion {}
task PostVersion {}
task PreBuild {}
task PostBuild {}
task PreTest {}
task PostTest {}
task PreTestReport {}
task PostTestReport {}
task PreAnalysis {}
task PostAnalysis {}
task PrePackage {}
task PostPackage {}
task PrePublish {}
task PostPublish {}
task RunLast {}