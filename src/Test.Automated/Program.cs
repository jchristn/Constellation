namespace Test.Automated
{
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    /// <summary>
    /// Touchstone CLI runner.  Executes every suite defined in Test.Shared and returns a
    /// CI-friendly exit code (0 = all passed, non-zero = at least one failure).
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            string resultsPath = args != null && args.Length > 0 ? args[0] : null;
            return await ConsoleRunner.RunAsync(ConstellationTestSuites.All, resultsPath: resultsPath);
        }
    }
}
