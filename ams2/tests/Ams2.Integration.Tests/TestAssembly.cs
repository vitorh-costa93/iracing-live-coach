using Xunit;

// Renderers and fake windows share the same GPU; run them one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
