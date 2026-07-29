# First load Endjin.RecommendedPractices.Build as we need the container tasks from here
# TODO: Replace with ZeroFailed extension when implemented

$zerofailedExtensions = @(
    @{
        Name = "Endjin.RecommendedPractices.Build"
        Version = "1.5.14"
    }
)
. ZeroFailed.tasks -ZfPath $here/.zf

# Then load the ZeroFailed extensions
$zerofailedExtensions = @(
    @{
        # References the extension from its GitHub repository. If not already installed, use latest version from 'main' will be downloaded.
        Name = "ZeroFailed.Build.DotNet"
        GitRepository = "https://github.com/zerofailed/ZeroFailed.Build.DotNet"
        GitRef = "main"
    }
)
. ZeroFailed.tasks -ZfPath $here/.zf

# Endjin.RecommendedPractices.Build 1.5.14 (loaded above) hard-pins the Covenant SBOM tool to 0.19.0,
# which predates Central Package Management support and reports "no components could be found" for this
# CPM solution, failing the build. Override it to a CPM-aware version. This assignment runs after both
# extensions load, so it wins over their defaults.
$covenantVersion = "0.24.0"

#
# Build process configuration
#
#
# Build process control options
#
$SkipInit = $false
$SkipVersion = $false
$SkipBuild = $false
$CleanBuild = $Clean
$SkipTest = $false
$SkipTestReport = $false
$SkipAnalysis = $false
$SkipPackage = $false

#
# Build process configuration
#
$SolutionToBuild = (Resolve-Path (Join-Path $here ".\Solutions\Ais.Net.Receiver.slnx")).Path
$ProjectsToPublish = @(
    # "Solutions/Ais.Net.Receiver.Host.Console/Ais.Net.Receiver.Host.Console.csproj"
)

$ContainerRegistryType = 'docker'
$ContainerRegistryPublishPrefix = 'endjin'  # publish the container images to the 'endjin' DockerHub namespace
$ContainersToBuild = @(
    @{
       Dockerfile = 'Solutions/Ais.Net.Receiver.Host.Console/Dockerfile'
       ImageName = 'ais-dotnet-receiver'
       ContextDir = "$here/Solutions"
       Arguments = @{ BUILD_CONFIGURATION = $Configuration; }
    }
)

# Ensure the old-style environment variable override expected by the old scripted build still works
$ContainerImageVersionOverride = property BUILDVAR_ContainerImageVersionOverride  'local'    # override the GitVersion-generated SemVer used for tagging container images
$DockerRegistryUsername = property BUILDVAR_DockerRegistryUsername ''
$NugetPublishSource = property BUILDVAR_NuGetPublishSource "$here/_local-nuget-feed"
$SkipContainerImages = [Convert]::ToBoolean((property BUILDVAR_SkipContainerImages $false))

# Handle not being able to set an empty environment variable
if ($ContainerImageVersionOverride -eq '**UNSET**') {
    $ContainerImageVersionOverride = ''
}

$UseAcrTasks = $false
$MinimumBicepCliVersion = '0.31.92'
$NuSpecFilesToPackage = @(
    # "Solutions/MySolution/MyProject/MyProject.nuspec"
)
$IncludeAssembliesInCodeCoverage = "Ais*"
$ExcludeAssembliesInCodeCoverage = "Ais*.Tests"

task . FullBuild

#
# Build Process Extensibility Points - uncomment and implement as required
#

# task RunFirst {}
# task PreInit {}
# task PostInit {}
# task PreVersion {}
# task PostVersion {}
# task PreBuild {}
# task PostBuild {}
# task PreTest {}
# task PostTest {}
# task PreTestReport {}
# task PostTestReport {}
# task PreAnalysis {}
# task PostAnalysis {}
# task PrePackage {}
# task PostPackage {}
# task PrePublish {}
# task PostPublish {}
# task RunLast {}

# TODO: These can be removed once the ZeroFailed container extension is implemented
task BuildContainerWrapper -If { !$SkipContainerImages } -After PackageCore BuildContainerImages,BuildBicepFiles
task PublishContainerWrapper -If { !$SkipContainerImages } -After PublishCore PublishContainerImages