<#
Copy next to a CLI project and edit the command-surface section.
#>

$cli = "sample-cli"
$applicationId = "app-$cli"
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "$cli.MetaCli"))

Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue

meta-cli.exe --new-workspace $workspace
meta-cli.exe add-application --workspace $workspace --id $applicationId --name $cli --executable-name $cli

meta-cli.exe add-value-arity --workspace $workspace --id arity-none --name None --min-value-count 0 --max-value-count 0
meta-cli.exe add-value-arity --workspace $workspace --id arity-one --name One --min-value-count 1 --max-value-count 1

meta-cli.exe add-value-shape --workspace $workspace --id shape-flag --name Flag --value-arity arity-none
meta-cli.exe add-value-shape --workspace $workspace --id shape-text --name Text --value-arity arity-one --value-label "<value>"
meta-cli.exe add-value-shape --workspace $workspace --id shape-path --name Path --value-arity arity-one --value-label "<path>" --allows-option-like-value true

meta-cli.exe add-duplicate-option-behavior --workspace $workspace --id duplicate-error --name Error
meta-cli.exe add-unknown-token-behavior --workspace $workspace --id unknown-error --name Error
meta-cli.exe add-parser-policy --workspace $workspace --id parser-default --application $applicationId --name Default --stop-parsing-token "--" --allows-equals-value-syntax true --allows-options-after-positionals false --allows-short-option-clusters false --duplicate-option-behavior duplicate-error --unknown-token-behavior unknown-error

# Add this CLI's command surface here.
#
# ID shape:
#   cmd-<route>
#   exec-<route>
#   param-<route>-<name>
#   option-<route>-<name>
#   token-<route>-<name>
#   pos-<route>-<name>
#   group-<route>-<name>
#
# Example:
# meta-cli.exe add-command --workspace $workspace --id cmd-show --application $applicationId --name show --token show
# meta-cli.exe add-executable-command --workspace $workspace --id exec-show --command cmd-show

meta-cli.exe show --workspace $workspace
