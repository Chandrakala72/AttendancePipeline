# run-upload.ps1
#
# Runs the attendance report uploader for yesterday's date.
# Usage: .\run-upload.ps1 [-FilePath ./transation_data.xlsx]

param(
    [string]$FilePath = "./transaction_data.xlsx"
)

$Yesterday = (Get-Date).AddDays(-1).ToString("yyyy-MM-dd")

Write-Host "Uploading '$FilePath' for date $Yesterday..."

dotnet run -- $FilePath $Yesterday