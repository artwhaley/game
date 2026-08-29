using System.Collections.ObjectModel;
using NUnit.Framework;
using TruthCardGame.ReferenceHost.Wpf;

namespace TruthCardGame.ReferenceHost.Wpf.Tests
{
    [TestFixture]
    public sealed class RunnerLogBufferTests
    {
        [Test]
        public void AppendRetainsTheLastTenThousandPhysicalLines()
        {
            var lines = new ObservableCollection<string>();
            for (var i = 0; i < RunnerLogBuffer.MaxLines + 1; i++)
                RunnerLogBuffer.Append(lines, "line " + i);

            Assert.That(lines, Has.Count.EqualTo(RunnerLogBuffer.MaxLines));
            Assert.That(lines[0], Is.EqualTo("line 1"));
            Assert.That(lines[RunnerLogBuffer.MaxLines - 1], Is.EqualTo("line 10000"));
        }

        [Test]
        public void ExportPreservesStructuredMultilineCardEntry()
        {
            var lines = new ObservableCollection<string>();
            RunnerLogBuffer.Append(lines, "CARD START\nTitle: Test\nBody:\nFull body text");

            var export = RunnerLogBuffer.Export(lines);
            Assert.That(export, Does.Contain("CARD START"));
            Assert.That(export, Does.Contain("Title: Test"));
            Assert.That(export, Does.Contain("Body:"));
            Assert.That(export, Does.Contain("Full body text"));
        }

        [Test]
        public void ClearOnlyClearsTheLogCollection()
        {
            var lines = new ObservableCollection<string> { "one", "two" };
            RunnerLogBuffer.Clear(lines);

            Assert.That(lines, Is.Empty);
        }
    }
}
