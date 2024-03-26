﻿using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Logging;
using Microsoft.Testing.Platform.Messages;
using Microsoft.Testing.Platform.TestHost;
using TUnit.Core;
using TUnit.Core.Interfaces;
using TUnit.Engine.Extensions;
using TUnit.Engine.Models;
using TUnit.Engine.Models.Properties;
using TimeoutException = TUnit.Core.Exceptions.TimeoutException;

namespace TUnit.Engine;

internal class SingleTestExecutor : IDataProducer
{
    private readonly IExtension _extension;
    private readonly MethodInvoker _methodInvoker;
    private readonly TestClassCreator _testClassCreator;
    private readonly TestMethodRetriever _testMethodRetriever;
    private readonly Disposer _disposer;
    private readonly ILogger<SingleTestExecutor> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ConsoleInterceptor _consoleInterceptor;
    private readonly IMessageBus _messageBus;

    public GroupedTests GroupedTests { get; private set; } = null!;

    public SingleTestExecutor(
        IExtension extension,
        MethodInvoker methodInvoker, 
        TestClassCreator testClassCreator,
        TestMethodRetriever testMethodRetriever,
        Disposer disposer,
        ILoggerFactory loggerFactory,
        CancellationTokenSource cancellationTokenSource,
        ConsoleInterceptor consoleInterceptor,
        IMessageBus messageBus)
    {
        _extension = extension;
        _methodInvoker = methodInvoker;
        _testClassCreator = testClassCreator;
        _testMethodRetriever = testMethodRetriever;
        _disposer = disposer;
        _logger = loggerFactory.CreateLogger<SingleTestExecutor>();
        _cancellationTokenSource = cancellationTokenSource;
        _consoleInterceptor = consoleInterceptor;
        _messageBus = messageBus;
    }
    
    private readonly ConcurrentDictionary<string, Task> _oneTimeSetUpRegistry = new();

    public async Task<TUnitTestResult> ExecuteTestAsync(TestNode testNode, TestSessionContext session)
    {
        if(_cancellationTokenSource.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;
            
            await _messageBus.PublishAsync(this, new TestNodeUpdateMessage(session.SessionUid, new TestNode
            {
                Uid = testNode.Uid,
                DisplayName = testNode.DisplayName,
                Properties = new PropertyBag(new CancelledTestNodeStateProperty())
            }));

            return new TUnitTestResult
            {
                Duration = TimeSpan.Zero,
                Start = now,
                End = now,
                ComputerName = Environment.MachineName,
                Exception = null,
                Status = Status.None,
            };
        }
        
        var start = DateTimeOffset.Now;

        await _messageBus.PublishAsync(this, new TestNodeUpdateMessage(session.SessionUid, new TestNode
            {
                Uid = testNode.Uid,
                DisplayName = testNode.DisplayName,
                Properties = new PropertyBag(new InProgressTestNodeStateProperty())
            }));

        var skipReason = testNode.GetProperty<SkipReasonProperty>()?.SkipReason;
        if (skipReason != null || !IsExplicitlyRun(testNode))
        {
            await _logger.LogInformationAsync($"Skipping {testNode.DisplayName}...");
            
            await _messageBus.PublishAsync(this, new TestNodeUpdateMessage(session.SessionUid, new TestNode
            {
                Uid = testNode.Uid,
                DisplayName = testNode.DisplayName,
                Properties = new PropertyBag
                (
                    new SkippedTestNodeStateProperty(skipReason)
                )
            }));
            
            return new TUnitTestResult
            {
                Duration = TimeSpan.Zero,
                Start = start,
                End = start,
                ComputerName = Environment.MachineName,
                Exception = null,
                Status = Status.Skipped,
            };
        }
        try
        {
            await Task.Run(async () =>
            {
                await ExecuteWithRetries(testContext, testInformation, () => RunTest(testNode));
            });

            var end = DateTimeOffset.Now;
            
            await _messageBus.PublishAsync(this, new TestNodeUpdateMessage(session.SessionUid, new TestNode
            {
                Uid = testNode.Uid,
                DisplayName = testNode.DisplayName,
                Properties = new PropertyBag
                (
                    new PassedTestNodeStateProperty(),
                    new TimingProperty(new TimingInfo(start, end, end-start))
                )
            }));
            
            return new TUnitTestResult
            {
                TestContext = testContext,
                Duration = end - start,
                Start = start,
                End = end,
                ComputerName = Environment.MachineName,
                Exception = null,
                Status = testContext!.SkipReason != null ? Status.Skipped : Status.Passed,
                Output = testContext?.GetConsoleOutput() ?? testContext!.FailReason ?? testContext.SkipReason
            };
        }
        catch (Exception e)
        {
            var end = DateTimeOffset.Now;

            await _messageBus.PublishAsync(this, new TestNodeUpdateMessage(session.SessionUid, new TestNode
            {
                Uid = testNode.Uid,
                DisplayName = testNode.DisplayName,
                Properties = new PropertyBag
                (
                    new FailedTestNodeStateProperty(e),
                    new TimingProperty(new TimingInfo(start, end, end-start))
                )
            }));
            
            var unitTestResult = new TUnitTestResult
            {
                Duration = end - start,
                Start = start,
                End = end,
                ComputerName = Environment.MachineName,
                Exception = e,
                Status = testContext?.SkipReason != null ? Status.Skipped : Status.Failed,
                Output = testContext?.GetConsoleOutput()
            };

            if (testContext != null)
            {
                testContext.Result = unitTestResult;
            }
            
            return unitTestResult;
        }
        finally
        {
            await _disposer.DisposeAsync(classInstance);

            lock (_consoleStandardOutLock)
            {
                testContext?.Dispose();
            }
        }
    }

    private static async Task RunTest(TestNode testNode)
    {
        await TestDictionary.GetTest(testNode.Uid).Invoke();
    }

    internal void SetAllTests(GroupedTests tests)
    {
        GroupedTests = tests;
    }

    private readonly object _consoleStandardOutLock = new();

    private bool IsExplicitlyRun(TestNode testNode)
    {
        // TODO: Re-implement
        // if (_testFilterProvider.IsFilteredTestRun)
        // {
        //     return true;
        // }

        var explicitFor = testNode.GetProperty<ExplicitForProperty>()?.ExplicitFor;

        if (string.IsNullOrEmpty(explicitFor))
        {
            // Isn't required to be 'Explicitly' run
            return true;
        }

        // If all tests being run are from the same "Explicit" attribute, e.g. same class or same method, then yes these have been run explicitly.
        return GroupedTests.AllTests.All(x => x.GetProperty<ExplicitForProperty>()?.ExplicitFor == explicitFor);
    }

    private async Task ExecuteWithRetries(TestContext testContext, TestInformation testInformation, Func<Task> testDelegate)
    {
        var retryCount = testInformation.RetryCount;
        
        // +1 for the original non-retry
        for (var i = 0; i < retryCount + 1; i++)
        {
            try
            {
                await ExecuteCore(testContext, testDelegate);
                break;
            }
            catch (Exception e)
            {
                if (i == retryCount 
                    || !await ShouldRetry(testInformation, e))
                {
                    throw;
                }
                
                await _logger.LogWarningAsync($"{testInformation.TestName} failed, retrying...");
            }
        }
    }

    private async Task<bool> ShouldRetry(TestInformation testInformation, Exception e)
    {
        try
        {
            var retryAttribute = testInformation.LazyRetryAttribute.Value;

            if (retryAttribute == null)
            {
                return false;
            }

            return await retryAttribute.ShouldRetry(testInformation, e);
        }
        catch (Exception exception)
        {
            await _logger.LogErrorAsync(exception);
            return false;
        }
    }

    private async Task ExecuteCore(TestContext testContext, Func<Task> testDelegate)
    {
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        var testInformation = testContext.TestInformation;
        
        testInformation.CurrentExecutionCount++;
        
        var testLevelCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);

        if (testInformation.Timeout != null && testInformation.Timeout.Value != default)
        {
            testLevelCancellationTokenSource.CancelAfter(testInformation.Timeout.Value);
        }

        testContext.CancellationTokenSource = testLevelCancellationTokenSource;

        try
        {
            await ExecuteTestMethodWithTimeout(testInformation, testDelegate, testLevelCancellationTokenSource);
        }
        finally
        {
            testLevelCancellationTokenSource.Dispose();
        }
    }

    private async Task ExecuteTestMethodWithTimeout(TestInformation testInformation, Func<Task> testDelegate,
        CancellationTokenSource cancellationTokenSource)
    {
        var methodResult = testDelegate();

        if (testInformation.Timeout == null || testInformation.Timeout.Value == default)
        {
            await methodResult;
            return;
        }
        
        var timeoutTask = Task.Delay(testInformation.Timeout.Value, cancellationTokenSource.Token)
            .ContinueWith(t =>
            {
                if (methodResult.IsCompleted)
                {
                    return;
                }
                
                throw new TimeoutException(testInformation);
            });

        await await Task.WhenAny(timeoutTask, methodResult);
    }


    public Task<bool> IsEnabledAsync()
    {
        return _extension.IsEnabledAsync();
    }

    public string Uid => _extension.Uid;

    public string Version => _extension.Version;

    public string DisplayName => _extension.DisplayName;

    public string Description => _extension.Description;
    
    public Type[] DataTypesProduced { get; } = [typeof(TestNodeUpdateMessage)];
}