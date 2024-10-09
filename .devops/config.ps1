$devopsExtensions = @(
    @{
        Name = "Endjin.RecommendedPractices.Build"
        Version = "[1.5.10,2.0)"
        Process = "tasks/build.process.ps1"
    }
)

# Load the tasks and process
. endjin-devops.tasks

#
# Build process configuration
#
$SolutionToBuild = (Resolve-Path (Join-Path $here "./Solutions/Ais.Net.Receiver.sln")).Path
$SkipBuildModuleVersionCheck = $true    # currently doesn't work properly with endjin-devops

# Set default build task
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