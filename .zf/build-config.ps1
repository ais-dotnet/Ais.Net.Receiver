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
$ContainerImageVersionOverride = 'local'    # override the GitVersion-generated SemVer used for tagging container images
$ContainersToBuild = @(
    @{
       Dockerfile = 'Solutions/Ais.Net.Receiver.Host.Console/Dockerfile'
       ImageName = 'ais-dotnet-receiver'
       ContextDir = "$here/Solutions"
       Arguments = @{ BUILD_CONFIGURATION = $Configuration; }
    }
)

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

task ApplyEnvironmentVariableOverridesWrapper -Before PreInit ApplyEnvironmentVariableOverrides

# TODO: These can be removed once the ZeroFailed container & bicep extensions are implemented
task BuildContainerWrapper -After PackageCore BuildContainerImages,BuildBicepFiles
task PublishContainerWrapper -After PublishCore PublishContainerImages