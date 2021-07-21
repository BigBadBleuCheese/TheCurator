Param(
    [string] $InstructionSet = "",
    [switch] $SkipTests
)

$emptyStr = ""
$activity = "Building for $(switch ($InstructionSet) {
    $emptyStr { "x64" }
    default { $InstructionSet }
})"
$steps = 4
if ($SkipTests) {
    $steps -= 1
}
$step = 0

#region Clean Release Directory
$step += 1
Write-Host -ForegroundColor Blue "$activity (Step $step of $steps)"
Write-Host -ForegroundColor Green "Cleaning..."
Remove-Item -Path "bin\Release" -Recurse -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item -Path "obj\Release" -Recurse -Confirm:$false -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
#endregion Clean Release Directory

#region Build
$step += 1
Write-Host -ForegroundColor Blue "$activity (Step $step of $steps)"
Write-Host -ForegroundColor Green "Building..."
if ($InstructionSet -eq "x86") {
    dotnet build ../TheCurator.sln --configuration Release --verbosity normal --no-incremental /p:InstructionSet=x86 /p:TreatWarningsAsErrors=true /warnaserror
} elseif ($InstructionSet -eq "arm64") {
    dotnet build ../TheCurator.sln --configuration Release --verbosity normal --no-incremental /p:InstructionSet=arm64 /p:TreatWarningsAsErrors=true /warnaserror
} else {
    dotnet build ../TheCurator.sln --configuration Release --verbosity normal --no-incremental /p:TreatWarningsAsErrors=true /warnaserror
}
if ($LASTEXITCODE -ne 0)
{
    Write-Host -ForegroundColor Red "Build failed!"
    exit
}
Write-Host -ForegroundColor Green "Built."
Start-Sleep -Seconds 1
#endregion Build

#region Execute unit tests
if ($SkipTests -eq $false) {
    $step += 1
    Write-Host -ForegroundColor Blue "$activity (Step $step of $steps)"
    Write-Host -ForegroundColor Green "Executing unit tests..."
    dotnet test ../TheCurator.sln --configuration Release --verbosity normal --no-build "--logger:Console;verbosity=normal"
    if ($LASTEXITCODE -ne 0)
    {
        Write-Host -ForegroundColor Red "Unit testing failed!"
        exit
    }
    Write-Host -ForegroundColor Green "Executed unit tests."
    Start-Sleep -Seconds 1
} else {
    Write-Host -ForegroundColor Yellow "WARNING: Unit tests skipped."
}
#endregion Execute unit tests

#region Publish
$step += 1
Write-Host -ForegroundColor Blue "$activity (Step $step of $steps)"
Write-Host -ForegroundColor Green "Publishing..."
if ($InstructionSet -eq "x86") {
    dotnet publish --configuration Release --verbosity normal --no-build /p:InstructionSet=x86 /p:TreatWarningsAsErrors=true /warnaserror
} elseif ($InstructionSet -eq "arm64") {
    dotnet publish --configuration Release --verbosity normal --no-build /p:InstructionSet=arm64 /p:TreatWarningsAsErrors=true /warnaserror
} else {
    dotnet publish --configuration Release --verbosity normal --no-build /p:TreatWarningsAsErrors=true /warnaserror
}
if ($LASTEXITCODE -ne 0)
{
    Write-Host -ForegroundColor Red "Publish failed!"
    exit
}
Write-Host -ForegroundColor Green "Published."
Start-Sleep -Seconds 1
#endregion Publish

Write-Host -ForegroundColor Blue "Build script execution complete."