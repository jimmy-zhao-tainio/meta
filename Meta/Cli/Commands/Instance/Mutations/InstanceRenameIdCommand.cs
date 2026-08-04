internal sealed partial class CliRuntime
{
    async Task<int> InstanceRenameIdAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var oldId = RequiredValue("OldId");
        var newId = RequiredValue("NewId");
        var options = ReadMutatingCommonOptions(commandArgs, startIndex: 5);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        try
        {
            var workspace = await OpenXmlWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.ContractVersion);
            var rowsTouched = CountRenameRecordRowsTouched(
                workspace.State,
                entityName,
                oldId);
            var operation = new Operation.RenameRecord(
                entityName,
                oldId,
                newId);

            return await ExecuteOperationsAgainstOpenedXmlWorkspaceAsync(
                    workspace,
                    new[] { operation },
                    "instance rename-id",
                    "instance id renamed",
                    buildSuccessDetails: results =>
                    {
                        var result = (RenameRecordResult)results.Single();
                        return new (string Key, string Value)[]
                        {
                            ("Workspace", workspace.RootPath),
                            ("Entity", result.EntityName),
                            ("From", result.OldId),
                            ("To", result.NewId),
                            ("Relationships updated", result.RelationshipValueCount.ToString()),
                            ("Rows touched", rowsTouched.ToString()),
                        };
                    })
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }

        static int CountRenameRecordRowsTouched(
            InMemoryWorkspace workspace,
            string entityName,
            string oldId)
        {
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                entityName + "\u001f" + oldId,
            };

            foreach (var sourceEntity in workspace.Model.Entities)
            {
                foreach (var relationship in sourceEntity.Relationships.Where(item =>
                             MetaName.Comparer.Equals(item.Entity, entityName)))
                {
                    if (!workspace.Instance.RecordsByEntity.TryGetValue(
                            sourceEntity.Name,
                            out var records))
                    {
                        continue;
                    }

                    var relationshipName = relationship.GetColumnName();
                    foreach (var record in records)
                    {
                        if (record.RelationshipIds.TryGetValue(
                                relationshipName,
                                out var targetId) &&
                            MetaIdentity.Comparer.Equals(targetId, oldId))
                        {
                            touched.Add(sourceEntity.Name + "\u001f" + record.Id);
                        }
                    }
                }
            }

            return touched.Count;
        }
    }
}
