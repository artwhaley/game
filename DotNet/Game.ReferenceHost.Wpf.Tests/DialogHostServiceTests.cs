using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Core;
using TruthCardGame.ReferenceHost.Wpf;

namespace TruthCardGame.ReferenceHost.Wpf.Tests
{
    [TestFixture]
    public sealed class DialogHostServiceTests
    {
        [Test]
        public async Task ShowAsync_RoutesText_ToInjectedPresenter()
        {
            string presented = null;
            var service = new DialogHostService((text, ct) =>
            {
                presented = text;
                return Task.CompletedTask;
            });

            await service.ShowAsync("Hello world", CancellationToken.None);

            Assert.AreEqual("Hello world", presented);
        }

        [Test]
        public async Task ShowAsync_WithoutPresenter_LogsLoudly_AndCompletesSafely()
        {
            var log = new ListLog();
            var service = new DialogHostService(null, log);

            await service.ShowAsync("Dropped line", CancellationToken.None); // must not throw

            StringAssert.Contains("no dialog presenter", log.ToString());
            StringAssert.Contains("Dropped line", log.ToString());
        }

        [Test]
        public async Task ShowAsync_PresenterFailure_IsLogged_NotPropagated()
        {
            var log = new ListLog();
            var service = new DialogHostService((text, ct) => throw new InvalidOperationException("boom"), log);

            await service.ShowAsync("Troubled line", CancellationToken.None); // must not throw

            StringAssert.Contains("boom", log.ToString());
            StringAssert.Contains("Troubled line", log.ToString());
            Assert.AreEqual(1, log.ErrorCount);
        }

        [Test]
        public async Task ShowAsync_PresenterCancellation_PropagatesAsCanceled()
        {
            var service = new DialogHostService((text, ct) => throw new OperationCanceledException(ct));
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await service.ShowAsync("X", new CancellationToken(true)));
        }

        private sealed class ListLog : IGameLog
        {
            private readonly List<string> _lines = new List<string>();
            public int ErrorCount;

            public void Info(string message) => _lines.Add(message);
            public void Warning(string message) => _lines.Add(message);
            public void Error(string message)
            {
                ErrorCount++;
                _lines.Add(message);
            }
            public override string ToString() => string.Join("\n", _lines);
        }
    }
}