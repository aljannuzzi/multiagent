param(
	[string]$module
)

Start-Process -WorkingDirectory "./build/$module" -FilePath (Get-ChildItem -Path "./build/$module" -Filter "*.exe" -Depth 1 | Select-Object -First 1).FullName