namespace Test.Nunit
{
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// NUnit adapter over the shared Touchstone suites.  Every test case defined in
    /// Test.Shared is surfaced as an individual NUnit test via TestCaseSource.
    /// </summary>
    [TestFixture]
    public sealed class ConstellationNunitTests
    {
        private static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(ConstellationTestSuites.All);
        }

        /// <summary>
        /// Execute a single shared test case.
        /// </summary>
        /// <param name="testCase">Test case to execute.</param>
        /// <returns>Task.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
