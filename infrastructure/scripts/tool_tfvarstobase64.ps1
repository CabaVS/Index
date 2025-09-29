param (
    [string]$Folder = "."
)

$folderPath = Resolve-Path -Path $Folder

$tfFiles = Get-ChildItem -Path $folderPath -Recurse -Include *.tfvars, *.tfbackend, backend.hcl

foreach ($file in $tfFiles) {
    Write-Output $file.Name
    $contentBytes = [System.IO.File]::ReadAllBytes($file.FullName)
    [Convert]::ToBase64String($contentBytes)
    Write-Output ""
}