<#
.SYNOPSIS
    Publishes the app for GitHub Pages (or any static host under a sub-path).

.DESCRIPTION
    Builds a release (AOT-compiled unless -NoAot), then prepares publish/wwwroot for GitHub Pages:
      - sets <base href> to the site's path (e.g. /TableAssigner/), with the trailing slash it needs
      - removes the pre-compressed index.html.br/.gz, which would still hold the old base
      - adds .nojekyll (otherwise Pages drops the _framework and _content folders)
      - adds .gitattributes "* -text" (otherwise git may change line endings and break Blazor's integrity checks)
      - copies index.html to 404.html (so deep links and reloads open the app)
    With -Target, the result is copied into a checkout of the gh-pages branch. Everything there is replaced except
    .git, pages/ (your guest pages) and CNAME. Nothing is committed or pushed.

.EXAMPLE
    ./publish.ps1 -BasePath /TableAssigner/ -Target C:\git\tableplanner-ghpages

.EXAMPLE
    ./publish.ps1 -BasePath / -NoAot      # a quick build for a site at the domain root
#>
param(
    # The path the site is served under: "/<repo-name>/" for a GitHub project site, "/" for a custom domain.
    [Parameter(Mandatory = $true)] [string] $BasePath,

    # A checkout of the gh-pages branch to copy the result into.
    [string] $Target,

    # Build without AOT: much quicker, but the solver is much slower in the browser.
    [switch] $NoAot,

    [string] $Output = "publish",

    # Shown in the app (e.g. 2026.11.12.42; the deploy workflow passes the date and run number). Empty: a development build.
    [string] $Version,

    # The commit the build is made from, shown with the version.
    [string] $Commit
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not $BasePath.StartsWith("/")) { $BasePath = "/" + $BasePath }
if (-not $BasePath.EndsWith("/")) { $BasePath = $BasePath + "/" }

$publishArgs = @("publish", "src/TablePlanner.Web", "-c", "Release", "-o", $Output)
if ($NoAot) { $publishArgs += "-p:RunAOTCompilation=false" }
if ($Version) { $publishArgs += "-p:BuildVersion=$Version" }
if ($Commit) { $publishArgs += "-p:BuildCommit=$Commit" }
Write-Host "dotnet $($publishArgs -join ' ')" -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$www = Join-Path $Output "wwwroot"
$index = Join-Path $www "index.html"
$html = [IO.File]::ReadAllText($index)
$updated = [regex]::Replace($html, '<base href="[^"]*"\s*/?>', "<base href=`"$BasePath`" />")
if ($updated -eq $html -and $html -notmatch [regex]::Escape("<base href=`"$BasePath`"")) { throw "No <base href> found in $index." }
[IO.File]::WriteAllText($index, $updated)

Remove-Item (Join-Path $www "index.html.br"), (Join-Path $www "index.html.gz") -ErrorAction SilentlyContinue
[IO.File]::WriteAllText((Join-Path $www ".nojekyll"), "")
[IO.File]::WriteAllText((Join-Path $www ".gitattributes"), "* -text`n")
Copy-Item $index (Join-Path $www "404.html") -Force
Write-Host "Prepared $www for $BasePath" -ForegroundColor Green

if ($Target) {
    if (-not (Test-Path (Join-Path $Target ".git"))) { throw "$Target is not a git checkout." }
    $keep = @(".git", "pages", "CNAME")
    Get-ChildItem $Target -Force | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Recurse -Force
    Get-ChildItem $www -Force | Copy-Item -Destination $Target -Recurse -Force
    Write-Host "Copied to $Target. Review, then commit and push the gh-pages branch:" -ForegroundColor Green
    Write-Host "  git -C `"$Target`" add -A"
    Write-Host "  git -C `"$Target`" commit -m `"Deploy`""
    Write-Host "  git -C `"$Target`" push"
}
