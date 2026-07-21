using Xunit;

// Several runtime singletons are process-global — EmbeddedAssetStore (security + local assets) and
// CarbonAssetScope — and CarbonApp.Start() reconfigures them. Tests that read that state (asset
// serving, CSP) otherwise race with the many tests that start an app, causing intermittent failures.
// The suite is fast, so run it sequentially rather than sprinkle shared collections across every test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
