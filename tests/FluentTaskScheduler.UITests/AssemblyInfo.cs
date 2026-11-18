// The app runs once per Windows session, and the tests share one desktop: one test at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
