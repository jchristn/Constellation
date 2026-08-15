namespace Test.Shared
{
    using System.Collections.Generic;
    using Test.Shared.Suites;
    using Touchstone.Core;

    /// <summary>
    /// Central source of truth for all Constellation test suites.  Every runner
    /// (Test.Automated CLI, Test.Xunit, Test.Nunit) executes exactly these suites.
    /// </summary>
    public static class ConstellationTestSuites
    {
        /// <summary>
        /// All suites, in execution order.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    CoreModelSuite.Build(),
                    ApiErrorSuite.Build(),
                    SerializationSuite.Build(),
                    SettingsSuite.Build(),
                    WorkerBaseSuite.Build(),
                    WorkerServiceSuite.Build(),
                    IntegrationSuite.Build(),
                };
            }
        }
    }
}
