namespace Test.Automated.Harness
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal assertion helpers that throw <see cref="AssertException"/> on failure.
    /// </summary>
    public static class Assert
    {
        /// <summary>
        /// Asserts a condition is true.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="message">A description used in the failure message.</param>
        public static void True(bool condition, string message = "")
        {
            if (!condition) throw new AssertException("Expected true. " + message);
        }

        /// <summary>
        /// Asserts a condition is false.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="message">A description used in the failure message.</param>
        public static void False(bool condition, string message = "")
        {
            if (condition) throw new AssertException("Expected false. " + message);
        }

        /// <summary>
        /// Asserts two values are equal.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="expected">The expected value.</param>
        /// <param name="actual">The actual value.</param>
        /// <param name="message">A description used in the failure message.</param>
        public static void Equal<T>(T expected, T actual, string message = "")
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertException("Expected '" + expected + "' but got '" + actual + "'. " + message);
        }

        /// <summary>
        /// Asserts a reference is null.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="message">A description used in the failure message.</param>
        public static void Null(object? value, string message = "")
        {
            if (value != null) throw new AssertException("Expected null. " + message);
        }

        /// <summary>
        /// Asserts a reference is not null.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="message">A description used in the failure message.</param>
        public static void NotNull(object? value, string message = "")
        {
            if (value == null) throw new AssertException("Expected non-null. " + message);
        }

        /// <summary>
        /// Asserts a string contains a substring.
        /// </summary>
        /// <param name="haystack">The string to search.</param>
        /// <param name="needle">The substring to find.</param>
        public static void Contains(string haystack, string needle)
        {
            if (haystack == null || !haystack.Contains(needle, StringComparison.Ordinal))
                throw new AssertException("Expected '" + haystack + "' to contain '" + needle + "'.");
        }

        /// <summary>
        /// Asserts that an action throws an exception of a given type.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The action.</param>
        public static void Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertException("Expected " + typeof(TException).Name + " but got " + ex.GetType().Name + ".");
            }

            throw new AssertException("Expected " + typeof(TException).Name + " but nothing was thrown.");
        }

        /// <summary>
        /// Asserts that an asynchronous action throws an exception of a given type.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The asynchronous action.</param>
        /// <returns>A task that completes when the assertion is evaluated.</returns>
        public static async Task ThrowsAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertException("Expected " + typeof(TException).Name + " but got " + ex.GetType().Name + ".");
            }

            throw new AssertException("Expected " + typeof(TException).Name + " but nothing was thrown.");
        }
    }
}
