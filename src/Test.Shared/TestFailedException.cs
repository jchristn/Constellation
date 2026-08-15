namespace Test.Shared
{
    using System;

    /// <summary>
    /// Exception thrown when a test assertion fails.  Touchstone treats a thrown
    /// exception from a test case delegate as a test failure.
    /// </summary>
    public class TestFailedException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Failure message.</param>
        public TestFailedException(string message) : base(message)
        {
        }
    }
}
