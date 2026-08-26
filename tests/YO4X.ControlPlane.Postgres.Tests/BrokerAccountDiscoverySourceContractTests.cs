namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class BrokerAccountDiscoverySourceContractTests
{
    private static readonly string[] DirectorySearchRelations =
        ["brokerdirectory.servers", "brokerdirectory.tenant_demo_approvals"];

    [Fact]
    public void AccountListIsAuthorizationFirstActorScopedBoundedAndRedacted()
    {
        string source = ReadSource();
        string method = Slice(
            source,
            "public async Task<IReadOnlyList<BrokerAccountView>> GetBrokerAccountsAsync(",
            "public async Task<IReadOnlyList<BrokerAccountRegistrationOption>>");

        Assert.Contains("BeginAuthorizedAsync(", method, StringComparison.Ordinal);
        Assert.Contains("account.tenant_id = @tenant_id", method, StringComparison.Ordinal);
        Assert.Contains("account.user_id = @user_id", method, StringComparison.Ordinal);
        Assert.Contains("account.state <> 'deleted'", method, StringComparison.Ordinal);
        Assert.Contains("limit @limit", method, StringComparison.Ordinal);
        Assert.Contains("BrokerAccountDiscoveryLimit = 100", source, StringComparison.Ordinal);
        Assert.DoesNotContain("binding_fingerprint", method, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential_reference", method, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", method, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistrationOptionsAuthorizeBeforeResolvingConfiguredOrTenantApprovedDemoProfiles()
    {
        string source = ReadSource();
        string entryPoint = Slice(
            source,
            "public async Task<IReadOnlyList<BrokerAccountRegistrationOption>>",
            "private async Task<IReadOnlyList<BrokerAccountRegistrationOption>>");
        string approvedList = ApprovedListMethod(source);

        // The session boundary has to be established before either branch runs,
        // otherwise the route could be used as an existence oracle for
        // configuration and directory data.
        int authorization = entryPoint.IndexOf("BeginAuthorizedAsync(", StringComparison.Ordinal);
        int branch = entryPoint.IndexOf("searchTerm is null", StringComparison.Ordinal);
        int approvedListCall = entryPoint.IndexOf(
            "ReadApprovedRegistrationOptionsAsync(",
            StringComparison.Ordinal);
        int searchCall = entryPoint.IndexOf("SearchBrokerServerDirectoryAsync(", StringComparison.Ordinal);
        Assert.True(authorization >= 0);
        Assert.True(branch > authorization);
        Assert.True(approvedListCall > authorization);
        Assert.True(searchCall > authorization);

        Assert.Contains("options.ApprovedBrokerProfileId", approvedList, StringComparison.Ordinal);
        Assert.Contains(
            "NormalizeBrokerServer(options.ApprovedBrokerServer)",
            approvedList,
            StringComparison.Ordinal);
        Assert.Contains("profile.state = 'approved'", approvedList, StringComparison.Ordinal);
        Assert.Contains("'demo' = any(profile.environment_support)", approvedList, StringComparison.Ordinal);
        Assert.Contains(
            "profile.id = @pinned_profile_id and profile.server_name = @pinned_server",
            approvedList,
            StringComparison.Ordinal);
        Assert.Contains("approval.tenant_id = @tenant_id", approvedList, StringComparison.Ordinal);
        Assert.Contains("approval.id is not null", approvedList, StringComparison.Ordinal);
        Assert.Contains("limit @limit", approvedList, StringComparison.Ordinal);
        Assert.Contains("BrokerAccountRegistrationOptionLimit = 100", source, StringComparison.Ordinal);
        Assert.Contains("BrokerAccountEnvironment.Demo", approvedList, StringComparison.Ordinal);
        Assert.Contains("Approved: true", approvedList, StringComparison.Ordinal);
        Assert.DoesNotContain("login", approvedList, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", approvedList, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fingerprint", approvedList, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectorySearchReadsOnlyTheTenantScopedDirectoryTables()
    {
        string source = ReadSource();
        string search = SearchMethod(source);

        // The imported directory is unvetted vendor data. The search path may
        // therefore read the directory and this tenant's own approvals, and
        // nothing else: reaching into governance or operations here would let a
        // search term probe rows the caller cannot otherwise see.
        Assert.Equal(DirectorySearchRelations, RelationsReadBy(search));
        Assert.Contains("approval.tenant_id = @tenant_id", search, StringComparison.Ordinal);
        Assert.Contains("strpos(directory_server.search_key, @query) > 0", search, StringComparison.Ordinal);
        Assert.Contains("limit @limit", search, StringComparison.Ordinal);
        Assert.Contains("BrokerServerDirectorySearchLimit = 50", source, StringComparison.Ordinal);
        Assert.DoesNotContain("governance.", search, StringComparison.Ordinal);
        Assert.DoesNotContain("operations.", search, StringComparison.Ordinal);
        Assert.DoesNotContain("identity.", search, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectorySearchNeverInventsABrokerProfileForAnUnapprovedServer()
    {
        string search = SearchMethod(ReadSource());

        // `approval.broker_profile_id` is the fourth projected column, so a row
        // without this tenant's approval yields a null profile and an
        // `Approved: false` option that the registration route cannot consume.
        Assert.Contains("approval.broker_profile_id", search, StringComparison.Ordinal);
        Assert.Contains("bool approved = !reader.IsDBNull(3);", search, StringComparison.Ordinal);
        Assert.Contains("approved ? reader.GetGuid(3) : null,", search, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryReadsNeverMutate()
    {
        string source = ReadSource();

        foreach (string verb in new[] { "insert into", "update ", "delete from", "for update" })
        {
            Assert.DoesNotContain(verb, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UnusableSearchTermsFallBackToTheApprovedListInsteadOfScanningTheDirectory()
    {
        string source = ReadSource();
        string normalize = Slice(
            source,
            "private static string? NormalizeDirectoryQuery(string? query)",
            "private static BrokerAccountView ReadBrokerAccountView(");

        Assert.Contains("BrokerServerDirectoryMinimumQueryLength = 2", source, StringComparison.Ordinal);
        Assert.Contains("BrokerServerDirectoryMaximumQueryLength = 100", source, StringComparison.Ordinal);
        Assert.Contains("query?.Trim().Normalize(NormalizationForm.FormC)", normalize, StringComparison.Ordinal);
        Assert.Contains("normalized.Any(char.IsControl)", normalize, StringComparison.Ordinal);
        Assert.Contains("return null;", normalize, StringComparison.Ordinal);
        Assert.Contains("normalized.ToLowerInvariant()", normalize, StringComparison.Ordinal);
    }

    private static string ApprovedListMethod(string source) => Slice(
        source,
        "private async Task<IReadOnlyList<BrokerAccountRegistrationOption>> ReadApprovedRegistrationOptionsAsync(",
        "private static async Task<IReadOnlyList<BrokerAccountRegistrationOption>> SearchBrokerServerDirectoryAsync(");

    private static string SearchMethod(string source) => Slice(
        source,
        "private static async Task<IReadOnlyList<BrokerAccountRegistrationOption>> SearchBrokerServerDirectoryAsync(",
        "private static string? NormalizeDirectoryQuery(string? query)");

    /// <summary>
    /// Collects every schema-qualified relation the SQL in <paramref name="method"/>
    /// selects from, so a test can assert on the complete read surface rather
    /// than on the presence of individual table names.
    /// </summary>
    private static SortedSet<string> RelationsReadBy(string method)
    {
        var relations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string line in method.Split('\n'))
        {
            string[] words = line.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int index = 0; index < words.Length - 1; index++)
            {
                bool isSource = words[index].Equals("from", StringComparison.Ordinal)
                    || words[index].Equals("join", StringComparison.Ordinal);
                if (isSource && words[index + 1].Contains('.', StringComparison.Ordinal))
                {
                    relations.Add(words[index + 1]);
                }
            }
        }

        Assert.NotEmpty(relations);
        return relations;
    }

    private static string ReadSource() => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "Infrastructure",
        "YO4X.ControlPlane.Postgres",
        "PostgresBrokerAccountDiscoveryReads.cs"));

    private static string Slice(string value, string start, string end)
    {
        int startIndex = value.IndexOf(start, StringComparison.Ordinal);
        int endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return value[startIndex..endIndex];
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
