using Xunit;

// Integration fixtures intentionally share the single disposable database selected by
// ODCA_TEST_ADMIN_CONNECTION. Running classes concurrently lets cleanup from one journey
// revoke identities/subscriptions used by another and produces non-deterministic 401s.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
