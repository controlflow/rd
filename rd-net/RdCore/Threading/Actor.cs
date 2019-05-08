#pragma warning disable 1998
using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using JetBrains.Core;
using JetBrains.Diagnostics;
using JetBrains.Lifetimes;
using JetBrains.Util;

#if !NET35
namespace JetBrains.Threading
{
  
  public class Actor<T> : ISendChannel<T>
  {
    [PublicAPI] public string Id;
    private readonly Func<T, Task> myProcessor;
    private readonly AsyncChannel<T> myChannel;
    private readonly ILog myLog;


    private readonly AsyncLocal<Unit> myInsideProcessingFlow = new AsyncLocal<Unit>(); 

    private long myTotalMessagesProcessed; //todo maybe someday we run in ARM we need Interlocked.Increment

    [PublicAPI] public ISendChannel<T> Channel => myChannel;

    [PublicAPI]
    public bool IsEmpty => myTotalMessagesProcessed == myChannel.TotalMessagesSent;

    public bool IsInsideProcessing => myInsideProcessingFlow.Value != null;

    public Actor(string id, Lifetime lifetime, Action<T> processor, TaskScheduler scheduler = null,
      int maxQueueSize = int.MaxValue) :
      this(id, lifetime, async item => processor(item), scheduler, maxQueueSize) {}
    
    public Actor(string id, Lifetime lifetime, Func<T, Task> processor, TaskScheduler scheduler = null, int maxQueueSize = int.MaxValue)
    {
      Id = id;
      myProcessor = processor;
      myLog = Log.GetLog<Actor<Unit>>().GetSublogger(id);
      myChannel = new AsyncChannel<T>(lifetime, maxQueueSize);

      Task.Factory.StartNew(async () =>
        {
          try
          {
            myInsideProcessingFlow.Value = Unit.Instance;
            while (lifetime.IsAlive)
              await Process();
          }
          finally
          {
            Assertion.Assert(IsEmpty, "Not empty: sent items: {0}, processed items: {1}", myChannel.TotalMessagesSent, myTotalMessagesProcessed);
          }
        }, lifetime, TaskCreationOptions.None, scheduler ?? TaskScheduler.Current);
    }

#if !NET35 && !NETSTANDARD
    [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
#endif    
    
    
    
    private async Task Process()
    {
      var receiveItemTask = myChannel.ReceiveAsync();
      
      var item = await receiveItemTask;
      try
      {
        await myProcessor(item);
      }
      catch (Exception e)
      {
        myLog.Error(e);
      }
      finally
      {
        myTotalMessagesProcessed++;
      }
    }

    public void Send(T msg) => myChannel.Send(msg);
    public Task SendAsync(T msg) => myChannel.SendAsync(msg);

    public async Task SendOrExecuteInline(T msg)
    {
      if (IsInsideProcessing)
      {
        await myProcessor(msg);
      }
      else
      {
        Send(msg);
      }
    } 

    public void WaitForEmpty()
    {
      if (IsEmpty)
        return;

      SpinWait.SpinUntil(() => IsEmpty);
    }
  }
}
#endif