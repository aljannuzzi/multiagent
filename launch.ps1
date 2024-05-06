param(
	[string]$module
)

Start-Process -WorkingDirectory "./o/$module" -FilePath (Get-ChildItem -Path "./o/$module" -Filter "*.exe" -Depth 1 | Select-Object -First 1).FullName