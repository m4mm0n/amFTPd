param(
    [string]$ChangelogPath = "CHANGELOG.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error "git is required to run this script."
}

# Current tag from GitHub Actions env (refs/tags/vX.Y.Z)
$currentTag = $env:GITHUB_REF -replace '^refs/tags/', ''
if (-not $currentTag) {
    Write-Error "GITHUB_REF is not a tag ref. Current value: $($env:GITHUB_REF)"
}

# Find previous tag (newest first, excluding current)
$tags = git tag --sort=-creatordate | Where-Object { $_ -ne $currentTag }
$previousTag = $tags | Select-Object -First 1

if (-not $previousTag) {
    # First tag in repo → diff against empty tree: use tag alone
    $range = $currentTag
} else {
    $range = "$previousTag..$currentTag"
}

Write-Host "Current tag:  $currentTag"
if ($previousTag) {
    Write-Host "Previous tag: $previousTag"
} else {
    Write-Host "Previous tag: <none>"
}
Write-Host "Diff range:   $range"

# ---------- helpers ----------

function Get-MethodDeclarations {
    param(
        [string[]]$Lines
    )

    # Heuristic C# method signature regex
    $methodRegex = '^\s*(public|private|protected|internal)(\s+(static|abstract|virtual|override|async|sealed|new|extern))*\s+[\w<>\[\],\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\('

    $methods = @()

    for ($i = 0; $i -lt $Lines.Length; $i++) {
        $line = $Lines[$i]
        if ($line -match $methodRegex) {
            $name = $matches[4]
            $methods += [PSCustomObject]@{
                Name = $name
                Line = $i + 1  # 1-based
            }
        }
    }

    $methods | Sort-Object Line
}

function Get-ChangedMethodNames {
    param(
        [string]$Range,
        [string]$File,
        [string]$CurrentTag,
        [string]$PreviousTag
    )

    # New version of the file at CURRENT_TAG
    $newFileContent = git show "$($CurrentTag):$File" 2>$null
    if (-not $newFileContent) {
        return @()
    }

    $newLines   = $newFileContent -split "`n"
    $newMethods = Get-MethodDeclarations -Lines $newLines

    # Diff hunks (unified=0 → just the changed lines)
    $diff = git diff --unified=0 $Range -- "$File"
    if (-not $diff) {
        return @()
    }

    $changedLines = New-Object System.Collections.Generic.HashSet[int]

    foreach ($line in ($diff -split "`n")) {
        if ($line -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@') {
            $start = [int]$matches[1]
            $len   = if ($matches[2]) { [int]$matches[2] } else { 1 }

            for ($l = $start; $l -lt $start + $len; $l++) {
                [void]$changedLines.Add($l)
            }
        }
    }

    if ($changedLines.Count -eq 0) {
        return @()
    }

    # Previous methods by name to detect added vs changed
    $prevMethods = @{}
    if ($PreviousTag) {
        $prevContent = git show "$($PreviousTag):$File" 2>$null
        if ($prevContent) {
            $prevLines = $prevContent -split "`n"
            Get-MethodDeclarations -Lines $prevLines | ForEach-Object {
                $prevMethods[$_.Name] = $true
            }
        }
    }

    $results = New-Object System.Collections.Generic.List[object]

    foreach ($m in $newMethods) {
        # method spans from its line to the next method's line-1
        $next = $newMethods |
            Where-Object { $_.Line -gt $m.Line } |
            Sort-Object Line |
            Select-Object -First 1

        $startLine = $m.Line
        $endLine   = if ($next) { $next.Line - 1 } else { [int]::MaxValue }

        $hasChange = $false
        foreach ($l in $changedLines) {
            if ($l -ge $startLine -and $l -le $endLine) {
                $hasChange = $true
                break
            }
        }

        if ($hasChange) {
            $status = if ($prevMethods.ContainsKey($m.Name)) { "changed" } else { "added" }
            $results.Add([PSCustomObject]@{
                Name   = $m.Name
                Status = $status
            })
        }
    }

    # Unique by name+status
    $results |
        Group-Object Name, Status |
        ForEach-Object { $_.Group[0] }
}

# ---------- collect diff info ----------

$nameStatusLines = git diff --name-status $range | Where-Object { $_ -match '^\w' }

$newFiles      = New-Object System.Collections.Generic.List[string]
$modifiedFiles = New-Object System.Collections.Generic.List[string]

foreach ($line in $nameStatusLines) {
    $parts = $line -split "`t"
    if ($parts.Count -lt 2) { continue }

    $status = $parts[0]
    $file   = $parts[1]

    switch ($status) {
        "A" { $newFiles.Add($file) }
        "M" { $modifiedFiles.Add($file) }
        default { }
    }
}

if ($newFiles.Count -eq 0 -and $modifiedFiles.Count -eq 0) {
    Write-Host "No meaningful changes detected for $range. Not updating changelog."
    exit 0
}

# ---------- build changelog section in your format ----------

$dateStr    = (Get-Date).ToString('dd.MM.yyyy')   # 11.12.2025 style
$headerLine = "[ $currentTag - $dateStr ]"
$underline  = ''.PadLeft($headerLine.Length, '^')

$sectionLines = New-Object System.Collections.Generic.List[string]

$sectionLines.Add($headerLine)
$sectionLines.Add($underline)
$sectionLines.Add("Whats new?")
$sectionLines.Add("* Automated changelog based on git diff between ${range}...")
$sectionLines.Add("")

if ($newFiles.Count -gt 0) {
    $sectionLines.Add("New files:")
    $sectionLines.Add("")
    foreach ($f in $newFiles) {
        $sectionLines.Add("+ $f")
    }
    $sectionLines.Add("")
}

if ($modifiedFiles.Count -gt 0) {
    foreach ($f in $modifiedFiles) {
        $sectionLines.Add("$($f):")

        if ($f.ToLower().EndsWith(".cs")) {
            $methods = Get-ChangedMethodNames -Range $range -File $f -CurrentTag $currentTag -PreviousTag $previousTag
            if ($methods.Count -gt 0) {
                foreach ($m in $methods) {
                    if ($m.Status -eq "added") {
                        $sectionLines.Add("+ $($m.Name) added...")
                    } else {
                        $sectionLines.Add("* $($m.Name) changed...")
                    }
                }
            }
            else {
                $sectionLines.Add("* changed...")
            }
        } else {
            $sectionLines.Add("* changed...")
        }

        $sectionLines.Add("") # blank line after file
    }
}

$newSection = ($sectionLines -join "`n") + "`n`n"

# ---------- prepend to CHANGELOG.md ----------

if (Test-Path $ChangelogPath) {
    $existing = Get-Content $ChangelogPath -Raw

    # Ensure there's a clean break between new section and old content
    if ([string]::IsNullOrWhiteSpace($existing)) {
        $combined = $newSection
    } else {
        $trimmedExisting = $existing.TrimStart("`r", "`n")
        $combined = $newSection + $trimmedExisting
    }

    Set-Content -Path $ChangelogPath -Value $combined
    Write-Host "Prepended new changelog section to $ChangelogPath"
}
else {
    Set-Content -Path $ChangelogPath -Value $newSection
    Write-Host "Created new $ChangelogPath"
}