﻿using System;
using System.Collections.Generic;
using System.Threading;
using JetBrains.Annotations;
using JetBrains.Util;

namespace JetBrains.Diagnostics
{

  public class LogLogRecord
  {
    public LogLogRecord(string category, LoggingLevel severity, [NotNull] string message)
    {
      Time = DateTime.Now.ToLocalTime(); //no need for UTC, because we want to display dates in local format
      Category = category;
      Severity = severity;
      Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public DateTime Time { get; }
    public string Category { get; }
    public LoggingLevel Severity { get; }

    [NotNull]
    public string Message { get; }

    public string Format(bool includeDate)
    {
      var categoryFormatted = "--LOGLOG" + (string.IsNullOrEmpty(Category) ? "" : ":" + Category + "")+ "--";
      return Log.DefaultFormat(includeDate ? Time.ToNullable() : null, Severity, categoryFormatted, Thread.CurrentThread, Message);
    }
  }

  /// <summary>
  /// To record messages and exceptions from logger (and logger referenced code) and don't fall into infinite recursion.
  /// </summary>
  public static class LogLog
  {

    private static volatile LoggingLevel ourSeverityFilter = LoggingLevel.VERBOSE;
    public static LoggingLevel SeverityFilter
    {
      get => ourSeverityFilter;
      set
      {
        var old = ourSeverityFilter;
        if (old != value)
        {
          ourSeverityFilter = value;
          Fire(null, "LogLog.SeverityFilter: " + old + " -> " + value, LoggingLevel.VERBOSE);
        }        
      }
    }
    private static readonly object ourLock = new object();


    #region Records event

    private static readonly List<Action<LogLogRecord>> ourEventListeners = new List<Action<LogLogRecord>>();

    public static event Action<LogLogRecord> RecordsChanged
    {
      add
      {
        lock (ourLock)
        {
         ourEventListeners.Add(value);
        }
      }
      remove
      {
        lock (ourLock)
        {
          ourEventListeners.Remove(value);
        }
      }
    }


    [ThreadStatic]
    private static bool ourReentrancyGuard;

    private static void Fire([CanBeNull] string category, [NotNull] string msg, LoggingLevel severity)
    {
      if (ourReentrancyGuard)
      {
        var stackTrace = Environment.StackTrace;
        
        Console.Error.WriteLine("Reentrancy in LogLog:\n " + stackTrace);
        return;
      }

      if (severity > SeverityFilter) return;

      var record = new LogLogRecord(
        category,
        severity,
        msg
      );

      //copy listeners
      var eventListenersCopy = new List<Action<LogLogRecord>>();
      lock (ourLock)
      {
        eventListenersCopy.AddRange(ourEventListeners);
      }


      try
      {
        ourReentrancyGuard = true;

        foreach (var listener in eventListenersCopy)
        {
          listener(record);
        }

      }
      finally
      {
        ourReentrancyGuard = false;
      }
    }
    #endregion




    #region Defalt listeners

    static LogLog()
    {
      RecordsChanged += RecordsStoreListener;
    }


    private static void RecordsStoreListener(LogLogRecord record)
    {
      lock (ourLock)
      {
        ourRecords.AddLast(record);
        while (StoredRecords.Count > MaxRecordsToStore) ourRecords.RemoveFirst();
      }
    }


    #endregion




    #region Records store


    /// <summary>
    /// Sliding window of records
    /// </summary>
    private const int MaxRecordsToStore = 100;



    //field for StoredRecords
    private static readonly LinkedList<LogLogRecord> ourRecords = new LinkedList<LogLogRecord>();

    /// <summary>
    /// We store last <see cref="MaxRecordsToStore"/> loglog records to browse
    /// </summary>
    public static List<LogLogRecord> StoredRecords
    {
      get
      {
        lock (ourLock)
        {
          return new List<LogLogRecord>(ourRecords);
        }
      }
    }

    #endregion


    #region API for logloging

    public static void Error([NotNull] Exception ex, string comment = null)
    {
      if (ex == null) throw new ArgumentException("ex is null");

      var msg = (string.IsNullOrEmpty(comment) ? "" : comment + " | ") + ex;
      Fire(null, msg, LoggingLevel.ERROR);
    }

    public static void Error([NotNull] string error)
    {
      if (error == null) throw new ArgumentNullException(nameof(error));

      var msg = new Exception(error).ToString();
      Fire(null, msg, LoggingLevel.ERROR);
    }
    
    [StringFormatMethod("format")]
    public static void Warn([NotNull] string format, params object[] args)
    {
      if (format == null) throw new ArgumentException("message is null");

      var msg = string.Format(format, args);
      Fire(null, msg, LoggingLevel.WARN);
    }

    [StringFormatMethod("format")]
    public static void Info([NotNull] string format, params object[] args)
    {
      if (format == null) throw new ArgumentException("message is null");

      var msg = string.Format(format, args);
      Fire(null, msg, LoggingLevel.INFO);
    }

    [StringFormatMethod("format")]
    public static void Verbose([CanBeNull] string category, [NotNull] string format, params object[] args)
    {
      if (format == null) throw new ArgumentException("message is null");

      var msg = string.Format(format, args);
      Fire(category, msg, LoggingLevel.VERBOSE);
    }
    
    [StringFormatMethod("format")]
    public static void Trace([CanBeNull] string category, [NotNull] string format, params object[] args)
    {
      if (format == null) throw new ArgumentException("message is null");

      if (SeverityFilter < LoggingLevel.TRACE)
        return; //shortcut to suppress string formatting
      
      var msg = string.Format(format, args);
      Fire(category, msg, LoggingLevel.TRACE);
    }
    
    [StringFormatMethod("format")]
    public static void Trace<T1>([CanBeNull] string category, [NotNull] string format, T1 arg) //to suppress array creation
    {
      if (format == null) throw new ArgumentException("message is null");

      if (SeverityFilter < LoggingLevel.TRACE)
        return; //shortcut to suppress string formatting
      
      var msg = string.Format(format, arg);
      Fire(category, msg, LoggingLevel.TRACE);
    }
    
    [StringFormatMethod("format")]
    public static void Trace<T1, T2>([CanBeNull] string category, [NotNull] string format, T1 arg1, T2 arg2) //to suppress array creation 
    {
      if (format == null) throw new ArgumentException("message is null");

      if (SeverityFilter < LoggingLevel.TRACE)
        return; //shortcut to suppress string formatting
      
      var msg = string.Format(format, arg1, arg2);
      Fire(category, msg, LoggingLevel.TRACE);
    }

#if !NET35 && !NETSTANDARD
    [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
#endif    
    
    public static void Catch([NotNull] string comment, [NotNull] Action action)
    {
      try
      {
        action();
      }
      catch (Exception e)
      {
        Error(e, comment);
      }
    }

#if !NET35 && !NETSTANDARD
    [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
#endif
    
    public static void Catch([NotNull] Action action)
    {
      try
      {
        action();
      }
      catch (Exception e)
      {
        Error(e);
      }
    }

#if !NET35 && !NETSTANDARD
    [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
#endif
    
    public static T Catch<T>([NotNull] Func<T> action)
    {
      try
      {
        return action();
      }
      catch (Exception e)
      {
        Error(e);
        return default(T);
      }
    }

    #endregion
  }
}