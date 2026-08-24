using Npgsql;
using NpgsqlTypes;
using YO4X.ControlPlane.Application;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    public async Task<StrategyCompatibilityProjection?> GetStrategyCompatibilityAsync(
        UserActor actor,
        Guid corpusId,
        CancellationToken cancellationToken)
    {
        (var transaction, _) = await BeginAuthorizedAsync(
                actor,
                Guid.CreateVersion7(),
                cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    corpus.file_count,
                    source_file.id,
                    source_file.manifest_order,
                    source_file.relative_path,
                    source_file.source_kind,
                    source_file.disposition,
                    pg_catalog.jsonb_array_length(source_file.features)
                from governance.strategy_source_corpora as corpus
                join governance.strategy_conversion_classifications as classification
                  on classification.tenant_id = corpus.tenant_id
                 and classification.corpus_id = corpus.id
                 and classification.user_id = corpus.user_id
                join governance.strategy_source_files as source_file
                  on source_file.tenant_id = corpus.tenant_id
                 and source_file.corpus_id = corpus.id
                 and source_file.user_id = corpus.user_id
                where corpus.tenant_id = @tenant_id
                  and corpus.user_id = @user_id
                  and corpus.id = @corpus_id
                  and corpus.state = 'static_analyzed'
                order by source_file.manifest_order
                """);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
            command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, actor.UserId);
            command.Parameters.AddWithValue("corpus_id", NpgsqlDbType.Uuid, corpusId);

            int? expectedFileCount = null;
            var items = new List<StrategyCompatibilityItem>();
            var strategyIds = new HashSet<Guid>();
            {
                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    int rowFileCount = reader.GetInt32(0);
                    if (rowFileCount is < 1 or > 10_000
                        || expectedFileCount is not null && expectedFileCount != rowFileCount)
                    {
                        throw new InvalidOperationException(
                            "The strategy compatibility projection has inconsistent corpus evidence.");
                    }

                    expectedFileCount = rowFileCount;
                    int manifestOrder = reader.GetInt32(2);
                    Guid strategyId = reader.GetGuid(1);
                    if (manifestOrder != items.Count
                        || manifestOrder >= rowFileCount
                        || strategyId == Guid.Empty
                        || !strategyIds.Add(strategyId))
                    {
                        throw new InvalidOperationException(
                            "The strategy compatibility projection has inconsistent file ordering.");
                    }

                    string relativePath = reader.GetString(3);
                    StrategyCompatibilitySourceType sourceType = ParseSourceType(reader.GetString(4));
                    items.Add(new StrategyCompatibilityItem(
                        strategyId,
                        DisplayName(relativePath, sourceType),
                        sourceType,
                        ParseAnalysisState(reader.GetString(5)),
                        reader.GetInt32(6),
                        ReportPath: null));
                }
            }

            if (expectedFileCount is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            if (items.Count != expectedFileCount)
            {
                throw new InvalidOperationException(
                    "The strategy compatibility projection is incomplete.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StrategyCompatibilityProjection(
                items.Count,
                expectedFileCount.Value,
                items.AsReadOnly());
        }
    }

    private static StrategyCompatibilitySourceType ParseSourceType(string value) => value switch
    {
        "expert_or_program" => StrategyCompatibilitySourceType.Mq5,
        "header" => StrategyCompatibilitySourceType.Mqh,
        _ => throw new InvalidOperationException(
            "An unknown strategy source type is persisted.")
    };

    private static StrategyCompatibilityAnalysisState ParseAnalysisState(string value) => value switch
    {
        "needs_semantic_validation" => StrategyCompatibilityAnalysisState.ReviewRequired,
        "needs_source" => StrategyCompatibilityAnalysisState.Pending,
        "unsupported" or "rejected" => StrategyCompatibilityAnalysisState.Unsupported,
        _ => throw new InvalidOperationException(
            "An unknown strategy compatibility disposition is persisted.")
    };

    private static string DisplayName(
        string relativePath,
        StrategyCompatibilitySourceType sourceType)
    {
        string extension = sourceType == StrategyCompatibilitySourceType.Mq5 ? ".mq5" : ".mqh";
        if (relativePath.Length <= extension.Length
            || !relativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The strategy compatibility projection has an inconsistent source path.");
        }

        return relativePath[..^extension.Length];
    }
}
