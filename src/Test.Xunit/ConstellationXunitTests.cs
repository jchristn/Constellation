namespace Test.Xunit
{
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using global::Xunit;

    /// <summary>
    /// xUnit adapter over the shared Touchstone suites.  Every non-skipped test case defined
    /// in Test.Shared is surfaced as an individual xUnit theory row.
    /// </summary>
    public sealed class ConstellationXunitTests
    {
        /// <summary>
        /// Theory data: every non-skipped test case across all shared suites.
        /// </summary>
        /// <returns>Theory data.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            TheoryData<TestCaseDescriptor> data = new TheoryData<TestCaseDescriptor>();
            foreach (TestSuiteDescriptor suite in ConstellationTestSuites.All)
            {
                foreach (TestCaseDescriptor testCase in suite.Cases)
                {
                    if (!testCase.Skip) data.Add(testCase);
                }
            }
            return data;
        }

        /// <summary>
        /// Execute a single shared test case.
        /// </summary>
        /// <param name="testCase">Test case to execute.</param>
        /// <returns>Task.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
