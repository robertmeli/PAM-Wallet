using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;

var baseUrl = args.ElementAtOrDefault(0) ?? "http://localhost:42792";
var durationSeconds = int.TryParse(args.ElementAtOrDefault(1), out var d) ? d : 300;
var targetRps = int.TryParse(args.ElementAtOrDefault(2), out var r) ? r : 1000;
var endpoint = (args.ElementAtOrDefault(3) ?? "add").ToLowerInvariant();
var playerCardinality = int.TryParse(args.ElementAtOrDefault(4), out var p) ? p : 10000;
var extraArgs = args.Skip(5)
    .Select(x => x.Trim().ToLowerInvariant())
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .ToArray();

var diagnosticsEnabled = extraArgs.Contains("diag");
var skipSeed = extraArgs.Any(x => x is "noseed" or "skipseed" or "skip-seed");
var requestTimeoutSeconds = 10;
var timeoutArg = extraArgs.FirstOrDefault(x => x.StartsWith("timeout=", StringComparison.Ordinal));
if (timeoutArg is not null)
{
    var value = timeoutArg["timeout=".Length..];
    if (int.TryParse(value, out var parsedTimeout) && parsedTimeout is >= 1 and <= 120)
        requestTimeoutSeconds = parsedTimeout;
}

if (playerCardinality < targetRps * 2)
{
    throw new InvalidOperationException(
        $"playerCardinality ({playerCardinality}) is too low for targetRps ({targetRps}). " +
        "Use cardinality at least 2x RPS to avoid hot-key grain serialization bottlenecks.");
}

var handler = new SocketsHttpHandler
{
    MaxConnectionsPerServer = 10_000,
    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
};

using var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(requestTimeoutSeconds)
};

using (var preflightCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    HttpResponseMessage? healthResponse = null;
    HttpResponseMessage? createProbeResponse = null;
    HttpResponseMessage? addProbeResponse = null;
    try
    {
        healthResponse = await httpClient.GetAsync("/health", preflightCts.Token);

        var probePlayerId = $"__perf_probe__{Guid.NewGuid():N}";
        using var createProbeBody = new StringContent("""{"walletType":"Main","currencyType":"EUR","expiresAt":"2030-01-01T00:00:00Z"}""", Encoding.UTF8, "application/json");
        createProbeResponse = await httpClient.PostAsync($"/wallets/{probePlayerId}", createProbeBody, preflightCts.Token);

        using var addProbeBody = new StringContent("""{"amount":1.00,"walletType":"Main","currencyType":"EUR","expiresAt":"2030-01-01T00:00:00Z"}""", Encoding.UTF8, "application/json");
        addProbeResponse = await httpClient.PostAsync($"/wallets/{probePlayerId}/funds/add", addProbeBody, preflightCts.Token);
    }
    catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
    {
        throw new InvalidOperationException(
            $"Wallet API preflight to '{baseUrl}' failed. Ensure Wallet.Api is running and pass the correct base URL as arg#1. " +
            "Examples: 'http://localhost:42792' when running Wallet.Api directly, or the wallet-api endpoint shown by AppHost.",
            ex);
    }

    if (!healthResponse.IsSuccessStatusCode)
    {
        throw new InvalidOperationException(
            $"Wallet API preflight to '{baseUrl}/health' returned {(int)healthResponse.StatusCode}. " +
            "Pass the Wallet.Api base URL as arg#1.");
    }

    var createProbeOk = createProbeResponse.StatusCode is System.Net.HttpStatusCode.Created or System.Net.HttpStatusCode.Conflict;
    if (!createProbeOk)
    {
        throw new InvalidOperationException(
            $"Wallet API preflight create probe to '{baseUrl}/wallets/{{probePlayerId}}' returned {(int)createProbeResponse.StatusCode}. " +
            "This target does not look like Wallet.Api. Pass the wallet-api endpoint URL (not AppHost/dashboard URL). ");
    }

    var addProbeIsJsonOk =
        addProbeResponse.StatusCode == System.Net.HttpStatusCode.OK &&
        addProbeResponse.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

    if (!addProbeIsJsonOk)
    {
        throw new InvalidOperationException(
            $"Wallet API preflight add_funds probe to '{baseUrl}/wallets/{{probePlayerId}}/funds/add' returned {(int)addProbeResponse.StatusCode}. " +
            "Target responded to health but wallet write path is not healthy. Ensure Wallet.Api and dependencies are fully up.");
    }
}

long existingPlayerCounter = 0;
long slowRequestCount = 0;
long verySlowRequestCount = 0;
var failureSamplePrintCount = 0;
var statusCodeCounts = new ConcurrentDictionary<int, int>();
var exceptionCounts = new ConcurrentDictionary<string, int>();

string NextExistingPlayerId() => $"perf-player-{Interlocked.Increment(ref existingPlayerCounter) % playerCardinality}";

void Increment<TKey>(ConcurrentDictionary<TKey, int> dictionary, TKey key) where TKey : notnull =>
    dictionary.AddOrUpdate(key, 1, (_, current) => current + 1);

string Truncate(string value, int maxLength) =>
    value.Length <= maxLength ? value : value[..maxLength] + "...";

async Task<IResponse> ExecuteWithDiagnostics(string operation, Func<Task<HttpResponseMessage>> send)
{
    var sw = Stopwatch.StartNew();

    try
    {
        using var response = await send();
        sw.Stop();

        if (diagnosticsEnabled)
        {
            Increment(statusCodeCounts, (int)response.StatusCode);

            if (sw.ElapsedMilliseconds >= 5_000)
                Interlocked.Increment(ref slowRequestCount);

            if (sw.ElapsedMilliseconds >= 30_000)
                Interlocked.Increment(ref verySlowRequestCount);
        }

        if (response.IsSuccessStatusCode)
            return Response.Ok();

        if (diagnosticsEnabled && Interlocked.Increment(ref failureSamplePrintCount) <= 10)
        {
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[diag] {operation} {(int)response.StatusCode} in {sw.ElapsedMilliseconds}ms :: {Truncate(body, 300)}");
        }

        return Response.Fail(statusCode: ((int)response.StatusCode).ToString());
    }
    catch (Exception ex)
    {
        sw.Stop();

        if (diagnosticsEnabled)
        {
            var key = $"{ex.GetType().Name}: {ex.Message}";
            Increment(exceptionCounts, key);

            if (Interlocked.Increment(ref failureSamplePrintCount) <= 10)
                Console.WriteLine($"[diag] {operation} EX in {sw.ElapsedMilliseconds}ms :: {ex.GetType().Name} - {ex.Message}");
        }

        return Response.Fail(statusCode: $"EX_{ex.GetType().Name}");
    }
}

var createWalletBody = """{"walletType":"Main","currencyType":"EUR","expiresAt":"2030-01-01T00:00:00Z"}""";
var addFundsBody = """{"amount":10.00,"walletType":"Main","currencyType":"EUR","expiresAt":"2030-01-01T00:00:00Z"}""";
var seedFundsBody = """{"amount":100.00,"walletType":"Main","currencyType":"EUR","expiresAt":"2030-01-01T00:00:00Z"}""";
var deductFundsBody = """{"amount":1.00}""";

var needsFundsSeed = endpoint is "all" or "deduct" or "debit" or "balance";
var shouldSeedWallets = true;
var shouldSeedFunds = needsFundsSeed && !skipSeed;

if (shouldSeedWallets)
{
    for (var i = 0; i < playerCardinality; i++)
    {
        var playerId = $"perf-player-{i}";

        using var createBody = new StringContent(createWalletBody, Encoding.UTF8, "application/json");
        var createResponse = await httpClient.PostAsync($"/wallets/{playerId}", createBody);
        if (!createResponse.IsSuccessStatusCode && createResponse.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException($"Failed to seed wallet for '{playerId}'. Status: {(int)createResponse.StatusCode}");
        }

        if (shouldSeedFunds)
        {
            using var addBody = new StringContent(seedFundsBody, Encoding.UTF8, "application/json");
            var addResponse = await httpClient.PostAsync($"/wallets/{playerId}/funds/add", addBody);
            if (!addResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to seed funds for '{playerId}'. Status: {(int)addResponse.StatusCode}");
            }
        }
    }
}

// ---- Scenarios -------------------------------------------------------

var addFundsScenario = Scenario.Create("add_funds_scenario", async _ =>
{
    var playerId = NextExistingPlayerId();
    return await ExecuteWithDiagnostics("add_funds", () =>
        httpClient.PostAsync($"/wallets/{playerId}/funds/add", new StringContent(addFundsBody, Encoding.UTF8, "application/json")));
})
.WithWarmUpDuration(TimeSpan.FromSeconds(60))
.WithLoadSimulations(
    Simulation.Inject(rate: targetRps, interval: TimeSpan.FromSeconds(1),
        during: TimeSpan.FromSeconds(durationSeconds)));

var deductFundsScenario = Scenario.Create("deduct_funds_scenario", async _ =>
{
    var playerId = NextExistingPlayerId();
    return await ExecuteWithDiagnostics("deduct_funds", () =>
        httpClient.PostAsync($"/wallets/{playerId}/funds/deduct", new StringContent(deductFundsBody, Encoding.UTF8, "application/json")));
})
.WithWarmUpDuration(TimeSpan.FromSeconds(30))
.WithLoadSimulations(
    Simulation.Inject(rate: targetRps, interval: TimeSpan.FromSeconds(1),
        during: TimeSpan.FromSeconds(durationSeconds)));

var getBalanceScenario = Scenario.Create("get_balance_scenario", async _ =>
{
    var playerId = NextExistingPlayerId();
    return await ExecuteWithDiagnostics("get_balance", () =>
        httpClient.GetAsync($"/wallets/{playerId}/balance"));
})
.WithWarmUpDuration(TimeSpan.FromSeconds(30))
.WithLoadSimulations(
    Simulation.Inject(rate: targetRps, interval: TimeSpan.FromSeconds(1),
        during: TimeSpan.FromSeconds(durationSeconds)));

// ---- Runner ----------------------------------------------------------

var scenarios = endpoint switch
{
    "add" or "credit" => new[] { addFundsScenario },
    "deduct" or "debit" => new[] { deductFundsScenario },
    "balance" => new[] { getBalanceScenario },
    _ => new[] { addFundsScenario, deductFundsScenario, getBalanceScenario }
};

NBomberRunner
    .RegisterScenarios(scenarios)
    .WithTestSuite("WalletPerformance")
    .WithTestName("PAM-Wallet Benchmark")
    .Run();

if (diagnosticsEnabled)
{
    Console.WriteLine("\n[diag] ===== HTTP status distribution =====");
    foreach (var kvp in statusCodeCounts.OrderBy(x => x.Key))
        Console.WriteLine($"[diag] {kvp.Key}: {kvp.Value}");

    Console.WriteLine("\n[diag] ===== Exception distribution =====");
    if (exceptionCounts.IsEmpty)
    {
        Console.WriteLine("[diag] none");
    }
    else
    {
        foreach (var kvp in exceptionCounts.OrderByDescending(x => x.Value).Take(10))
            Console.WriteLine($"[diag] {kvp.Value}x {kvp.Key}");
    }

    Console.WriteLine("\n[diag] ===== Slow request counters =====");
    Console.WriteLine($"[diag] >=5s : {slowRequestCount}");
    Console.WriteLine($"[diag] >=30s: {verySlowRequestCount}");
    Console.WriteLine("[diag] Flags: diag, noseed|skip-seed, timeout=<seconds>. Example: ... add 10000 diag noseed timeout=10");
}
