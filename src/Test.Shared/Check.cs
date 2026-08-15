namespace Test.Shared
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Lightweight assertion helpers.  Each method throws a <see cref="TestFailedException"/>
    /// when the assertion is not satisfied, which Touchstone records as a test failure.
    /// </summary>
    public static class Check
    {
        /// <summary>
        /// Assert that a condition is true.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Message describing the expectation.</param>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new TestFailedException("Expected true: " + message);
        }

        /// <summary>
        /// Assert that a condition is false.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Message describing the expectation.</param>
        public static void False(bool condition, string message)
        {
            if (condition) throw new TestFailedException("Expected false: " + message);
        }

        /// <summary>
        /// Assert that two values are equal.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="message">Message describing the expectation.</param>
        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailedException($"{message}: expected '{expected}', got '{actual}'");
        }

        /// <summary>
        /// Assert that two values are not equal.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="notExpected">Value that should not be observed.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="message">Message describing the expectation.</param>
        public static void NotEqual<T>(T notExpected, T actual, string message)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
                throw new TestFailedException($"{message}: expected value different from '{notExpected}'");
        }

        /// <summary>
        /// Assert that a reference is null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="message">Message describing the expectation.</param>
        public static void Null(object value, string message)
        {
            if (value != null) throw new TestFailedException("Expected null: " + message);
        }

        /// <summary>
        /// Assert that a reference is not null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="message">Message describing the expectation.</param>
        public static void NotNull(object value, string message)
        {
            if (value == null) throw new TestFailedException("Expected non-null: " + message);
        }

        /// <summary>
        /// Assert that a string contains a substring.
        /// </summary>
        /// <param name="substring">Substring that must be present.</param>
        /// <param name="text">Text to inspect.</param>
        /// <param name="message">Message describing the expectation.</param>
        public static void Contains(string substring, string text, string message)
        {
            if (text == null || !text.Contains(substring))
                throw new TestFailedException($"{message}: expected text to contain '{substring}', got '{text}'");
        }

        /// <summary>
        /// Assert that invoking an action throws an exception of the specified type.
        /// </summary>
        /// <typeparam name="T">Expected exception type.</typeparam>
        /// <param name="action">Action to invoke.</param>
        /// <param name="message">Message describing the expectation.</param>
        public static void Throws<T>(Action action, string message) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (Exception e)
            {
                throw new TestFailedException($"{message}: expected {typeof(T).Name}, got {e.GetType().Name} ({e.Message})");
            }

            throw new TestFailedException($"{message}: expected {typeof(T).Name}, but no exception was thrown");
        }
    }
}
