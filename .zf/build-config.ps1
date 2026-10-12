$zerofailedExtensions = @(
    @{
        # References the extension from its GitHub repository. If not already installed, use latest version from 'main' will be downloaded.
        Name = "ZeroFailed.Build.DotNet"
        GitRepository = "https://github.com/zerofailed/ZeroFailed.Build.DotNet"
        GitRef = "main"
    }
    @{
        # Builds and publishes the container images; hooks itself into the 'package' and 'publish' stages.
        Name = "ZeroFailed.Build.Containers"
        GitRepository = "https://github.com/zerofailed/ZeroFailed.Build.Containers"
        GitRef = "main"
    }
)
. ZeroFailed.tasks -ZfPath $here/.zf

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

#
# Container images (ZeroFailed.Build.Containers)
#
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

# Ensure the old-style environment variable overrides expected by the CI workflow still work. These are
# assigned after the extensions load, so they take precedence over the extension's own ZF_BUILD_* variables.
$ContainerImageVersionOverride = property BUILDVAR_ContainerImageVersionOverride  'local'    # override the GitVersion-generated SemVer used for tagging container images
$DockerRegistryUsername = property BUILDVAR_DockerRegistryUsername ''
$DockerRegistryPassword = property DOCKERHUB_ACCESSTOKEN ''
$NugetPublishSource = property BUILDVAR_NuGetPublishSource "$here/_local-nuget-feed"
$SkipContainerImages = [Convert]::ToBoolean((property BUILDVAR_SkipContainerImages $false))
$SkipBuildContainerImages = $SkipContainerImages
$SkipPublishContainerImages = $SkipContainerImages

# Handle not being able to set an empty environment variable
if ($ContainerImageVersionOverride -eq '**UNSET**') {
    $ContainerImageVersionOverride = ''
}

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
