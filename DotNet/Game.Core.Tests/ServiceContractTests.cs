using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Core;

namespace TruthCardGame.Core.Tests
{
    public class ServiceContractTests
    {
        private sealed class FakeDelay : IGameDelay
        {
            public int Calls;
            public TimeSpan LastDelay;

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                Calls++;
                LastDelay = delay;
                return Task.CompletedTask;
            }
        }

        private sealed class FakeLog : IGameLog
        {
            public readonly List<string> Entries = new List<string>();
            public void Info(string message) => Entries.Add("info:" + message);
            public void Warning(string message) => Entries.Add("warn:" + message);
            public void Error(string message) => Entries.Add("error:" + message);
        }

        private sealed class FakePrompts : IPromptService
        {
            public int? Answer = 0;
            public List<string> LastOptions;

            public Task<int?> AskAsync(string prompt, IReadOnlyList<string> options, CancellationToken cancellationToken)
            {
                LastOptions = new List<string>(options);
                return Task.FromResult(Answer);
            }
        }

        private sealed class FakeCutscenes : ICutsceneService
        {
            public string LastResourceId;
            public Task PlayAsync(string resourceId, CancellationToken cancellationToken)
            {
                LastResourceId = resourceId;
                return Task.CompletedTask;
            }
        }

        [Test]
        public void Services_SatisfiedByHandWrittenFakes()
        {
            var delay = new FakeDelay();
            var log = new FakeLog();
            var prompts = new FakePrompts();
            var cutscenes = new FakeCutscenes();
            var services = new CoreServices(delay, log, prompts, cutscenes);

            Assert.AreSame(delay, services.Delay);
            Assert.AreSame(log, services.Log);
            Assert.AreSame(prompts, services.Prompts);
            Assert.AreSame(cutscenes, services.Cutscene);
        }

        [Test]
        public void Services_NullLog_BecomesNullGameLog()
        {
            var services = new CoreServices(new FakeDelay());
            Assert.IsInstanceOf<NullGameLog>(services.Log);
            Assert.IsNull(services.Prompts);
            Assert.IsNull(services.Cutscene);
        }

        [Test]
        public void Services_NullDelay_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CoreServices(null));
        }

        [Test]
        public void Context_RequiresPlayerAndServices()
        {
            var services = new CoreServices(new FakeDelay());
            var context = new GameContext(new Player("p"), services);

            Assert.AreEqual("p", context.Player.Name);
            Assert.AreSame(services, context.Services);
            Assert.Throws<ArgumentNullException>(() => new GameContext(null, services));
            Assert.Throws<ArgumentNullException>(() => new GameContext(new Player("p"), null));
        }

        [Test]
        public async System.Threading.Tasks.Task Fakes_BehavePerContract()
        {
            var log = new FakeLog();
            var prompts = new FakePrompts { Answer = 1 };
            var cutscenes = new FakeCutscenes();
            var services = new CoreServices(new FakeDelay(), log, prompts, cutscenes);

            await services.Delay.DelayAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
            await services.Prompts.AskAsync("q", new[] { "a", "b" }, CancellationToken.None);
            await services.Cutscene.PlayAsync("res:1", CancellationToken.None);
            services.Log.Error("boom");

            Assert.AreEqual(2.0, ((FakeDelay)services.Delay).LastDelay.TotalSeconds);
            CollectionAssert.AreEqual(new[] { "a", "b" }, prompts.LastOptions);
            Assert.AreEqual(1, await prompts.AskAsync("again", new[] { "x" }, CancellationToken.None));
            Assert.AreEqual("res:1", cutscenes.LastResourceId);
            CollectionAssert.AreEqual(new[] { "error:boom" }, log.Entries);
        }
    }
}
