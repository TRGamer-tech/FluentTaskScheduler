using FluentTaskScheduler.Services;

namespace FluentTaskScheduler.Tests
{
    // Covers item 1.10: task names / event sources containing quotes must not break the XPath
    // queries built for GetTaskHistory / CreateEventTrigger.
    public class XPathLiteralTests
    {
        [Fact]
        public void PlainValue_IsWrappedInSingleQuotes()
        {
            Assert.Equal("'My Task'", TaskServiceWrapper.ToXPathLiteral("My Task"));
        }

        [Fact]
        public void ValueContainingSingleQuote_IsWrappedInDoubleQuotes()
        {
            Assert.Equal("\"O'Brien's backup\"", TaskServiceWrapper.ToXPathLiteral("O'Brien's backup"));
        }

        [Fact]
        public void ValueContainingBothQuoteTypes_UsesConcat()
        {
            string result = TaskServiceWrapper.ToXPathLiteral("a'b\"c");

            // Must not contain a raw, unescaped single-quote run that would terminate the literal early.
            Assert.StartsWith("concat(", result);
            Assert.Contains("'a'", result);
            Assert.Contains("\"'\"", result); // the embedded single quote, expressed as a double-quoted literal
            Assert.Contains("'b\"c'", result);
        }

        [Fact]
        public void EmptyValue_ProducesValidEmptyLiteral()
        {
            Assert.Equal("''", TaskServiceWrapper.ToXPathLiteral(""));
        }

        [Fact]
        public void NullValue_DoesNotThrow()
        {
            Assert.Equal("''", TaskServiceWrapper.ToXPathLiteral(null!));
        }
    }
}
