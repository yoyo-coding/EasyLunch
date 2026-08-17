# Stop and unregister the development loose package for StartPage.
$ErrorActionPreference = 'Stop'

Get-Process StartPage -ErrorAction SilentlyContinue | Stop-Process -Force
Get-AppxPackage | Where-Object { $_.Name -eq '9efc542c-499a-48fa-a5b2-40b523124659' } | Remove-AppxPackage
Write-Host 'StartPage development package unregistered.'
