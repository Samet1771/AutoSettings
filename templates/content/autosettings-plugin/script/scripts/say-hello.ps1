# The request (operation, parameters, event, user) arrives as JSON on standard input.
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
$name = $request.parameters.name
if (-not $name) {
    # A non-zero exit code makes the action fail; this text is shown in the Activity page.
    [Console]::Error.WriteLine("name is empty")
    exit 1
}
# Whatever the script prints is shown in the Activity page.
"Hello, $name!"
