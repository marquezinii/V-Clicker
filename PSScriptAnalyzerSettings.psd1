@{
    # build.ps1 and scripts/ talk to a person at a console; Write-Host is the right tool for that.
    ExcludeRules = @('PSAvoidUsingWriteHost')
    Severity     = @('Error', 'Warning')
}
