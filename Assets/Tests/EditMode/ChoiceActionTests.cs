using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// EditMode tests for ChoiceAction. Verifies the branching contract:
    /// the action yields on the prompt, resolves to the chosen child, runs a
    /// blocking child to completion or dispatches a continuous one, and fails
    /// loudly on a missing prompt service or zero options.
    ///
    /// Manual coroutine stepping cannot drive nested `yield return
    /// enumerator` (that's Unity's coroutine runner's job), so the tests use a
    /// small driver that mimics it: when a step yields an IEnumerator, the
    /// driver runs that child to completion before resuming the parent.
    /// </summary>
    public class ChoiceActionTests
    {
        /// <summary>Instant action — completes on its first step.</summary>
        private sealed class InstantAction : CardAction
        {
            public string Id;
            public List<string> Order;

            public override IEnumerator Execute(GameContext context)
            {
                Order.Add(Id + "-ran");
                yield break;
            }
        }

        /// <summary>Two-step action that records start, pauses, then ends.</summary>
        private sealed class TestAction : CardAction
        {
            public string Id;
            public List<string> Order;

            public override IEnumerator Execute(GameContext context)
            {
                Order.Add(Id + "-start");
                yield return null;
                Order.Add(Id + "-end");
            }
        }

        private sealed class RecordingRunner : ICoroutineRunner
        {
            public readonly List<IEnumerator> Routines = new List<IEnumerator>();

            public void StartRoutine(IEnumerator routine)
            {
                Routines.Add(routine);
                routine.MoveNext();
            }
        }

        /// <summary>Fake prompt service: captures the callback and handle, resolves both on Choose.</summary>
        private sealed class FakePromptService : IPromptService
        {
            public string LastPrompt;
            public List<string> LastOptions;
            public Action<int> LastCallback;
            private PromptHandle _handle;

            public CustomYieldInstruction Ask(string prompt, IReadOnlyList<string> options, Action<int> onChosen)
            {
                LastPrompt = prompt;
                LastOptions = options.ToList();
                LastCallback = onChosen;
                _handle = new PromptHandle();
                return _handle;
            }

            public void Choose(int index)
            {
                _handle?.Resolve();
                LastCallback?.Invoke(index);
            }
        }

        /// <summary>
        /// Steps a coroutine like Unity's runner: nested IEnumerator yields are
        /// driven to completion before the parent resumes; CustomYieldInstruction
        /// yields suspend the driver (Step returns true) until advanced again.
        /// </summary>
        private sealed class Driver
        {
            private readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();

            public void Push(IEnumerator routine)
            {
                _stack.Push(routine);
            }

            /// <summary>Advances until the routine suspends on a non-enumerator yield, or completes.</summary>
            public bool Step()
            {
                while (_stack.Count > 0)
                {
                    var current = _stack.Peek();
                    if (!current.MoveNext())
                    {
                        _stack.Pop();
                        continue;
                    }
                    if (current.Current is IEnumerator nested)
                    {
                        _stack.Push(nested);
                        continue;
                    }
                    // Suspended on something else (null, etc.) — caller decides.
                    return true;
                }
                return false;
            }

            public bool Done => _stack.Count == 0;
        }

        private static ChoiceAction MakeChoice(string[] labels, CardAction[] children)
        {
            var action = ScriptableObject.CreateInstance<ChoiceAction>();
            var so = new SerializedObject(action);
            so.FindProperty("prompt").stringValue = "Pick one";
            var optionsProp = so.FindProperty("options");
            optionsProp.arraySize = labels.Length;
            for (var i = 0; i < labels.Length; i++)
            {
                var element = optionsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("label").stringValue = labels[i];
                element.FindPropertyRelative("action").objectReferenceValue = children[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return action;
        }

        private static ChoiceAction MakeNoOptionChoice()
        {
            var action = ScriptableObject.CreateInstance<ChoiceAction>();
            var so = new SerializedObject(action);
            so.FindProperty("prompt").stringValue = "Pick one";
            so.FindProperty("options").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            return action;
        }

        private static GameContext MakeContext(IPromptService prompts, ICoroutineRunner runner)
        {
            var services = new GameServices(prompts: prompts, runner: runner);
            return new GameContext(new Player("choice-test"), services);
        }

        /// <summary>Steps the routine until it completes (bounded) and returns the step count.</summary>
        private static int Drain(IEnumerator routine)
        {
            var steps = 0;
            while (routine.MoveNext())
            {
                steps++;
                if (steps > 20) break;
            }
            return steps;
        }

        [Test]
        public void Execute_YieldsOnPrompt_AndRunsChosenBlockingChild()
        {
            var order = new List<string>();
            var childA = ScriptableObject.CreateInstance<InstantAction>();
            childA.Id = "a"; childA.Order = order;
            var childB = ScriptableObject.CreateInstance<InstantAction>();
            childB.Id = "b"; childB.Order = order;
            var choice = MakeChoice(new[] { "Option A", "Option B" }, new[] { childA, childB });
            var prompts = new FakePromptService();
            var driver = new Driver();
            driver.Push(choice.Execute(MakeContext(prompts, new RecordingRunner())));

            // Step: prompt shown, action suspended.
            Assert.IsTrue(driver.Step());
            Assert.AreEqual("Pick one", prompts.LastPrompt);
            Assert.AreEqual(new[] { "Option A", "Option B" }, prompts.LastOptions);
            Assert.IsFalse(driver.Done);

            // Player chooses option B (index 1) → child B runs to completion.
            prompts.Choose(1);
            Assert.IsFalse(driver.Step());
            CollectionAssert.AreEqual(new[] { "b-ran" }, order);
        }

        [Test]
        public void Execute_DispatchesContinuousChild_WithoutWaiting()
        {
            var order = new List<string>();
            var continuous = ScriptableObject.CreateInstance<TestAction>();
            continuous.Id = "c"; continuous.Order = order;
            var so = new SerializedObject(continuous);
            so.FindProperty("isBlocking").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            var choice = MakeChoice(new[] { "Go" }, new[] { continuous });
            var prompts = new FakePromptService();
            var runner = new RecordingRunner();
            var driver = new Driver();
            driver.Push(choice.Execute(MakeContext(prompts, runner)));

            Assert.IsTrue(driver.Step()); // prompt shown
            prompts.Choose(0);            // choose the continuous child

            // The choice action returns; the child was dispatched, not awaited.
            Assert.IsFalse(driver.Step());
            Assert.AreEqual(1, runner.Routines.Count);
            CollectionAssert.AreEqual(new[] { "c-start" }, order);
        }

        [Test]
        public void Execute_WithoutPromptService_FailsLoudly()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no IPromptService"));
            var child = ScriptableObject.CreateInstance<InstantAction>();
            child.Order = new List<string>();
            var choice = MakeChoice(new[] { "Only" }, new[] { child });
            var routine = choice.Execute(MakeContext(null, null));
            var steps = Drain(routine);
            Assert.LessOrEqual(steps, 3);
        }

        [Test]
        public void Execute_WithNoOptions_FailsLoudly()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no options"));
            var choice = MakeNoOptionChoice();
            var routine = choice.Execute(MakeContext(new FakePromptService(), new RecordingRunner()));
            var steps = Drain(routine);
            Assert.LessOrEqual(steps, 3);
        }
    }
}