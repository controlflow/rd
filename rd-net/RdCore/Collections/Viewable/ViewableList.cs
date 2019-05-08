﻿using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using JetBrains.Diagnostics;
using JetBrains.Lifetimes;

namespace JetBrains.Collections.Viewable
{
  public class ViewableList<T> : IViewableList<T>
  {
    private readonly IList<T> myStorage = new List<T>();
    private readonly Signal<ListEvent<T>> myChange = new Signal<ListEvent<T>>();

    [PublicAPI]
    public ISource<ListEvent<T>> Change
    {
      get { return myChange; }
    }

    public ViewableList(bool isReadonly = false)
    {
      IsReadOnly = isReadonly;
    }

    public IEnumerator<T> GetEnumerator()
    {
      return myStorage.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }

    // ReSharper disable once AnnotationConflictInHierarchy
    public void Add([NotNull] T item)
    {
      if (item == null) throw new ArgumentNullException(nameof(item));
      myStorage.Add(item);
      myChange.Fire(ListEvent<T>.Add(myStorage.Count-1, item));
    }

    public void Clear()
    {
      for (int index = myStorage.Count - 1; index >= 0; index--) 
        RemoveAt(index);
    }

    public bool Contains(T item)
    {
      return myStorage.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
      myStorage.CopyTo(array, arrayIndex);
    }

    public bool Remove(T item)
    {
      var index = myStorage.IndexOf(item);
      if (index < 0) return false;

      RemoveAt(index);
      return true;
    }

    public int Count
    {
      get { return myStorage.Count; }
    }

    public bool IsReadOnly { get; private set; }    
    

    public void RemoveAt(int index)
    {
      var old = myStorage[index];
      myStorage.RemoveAt(index);
      myChange.Fire(ListEvent<T>.Remove(index, old));
    }
    

    [NotNull]
    public T this[int index]
    {
      get { return myStorage[index]; }
      set
      {
        Assertion.Require(value != null, "value != null");
        
        var oldval = myStorage[index];
        if (Equals(oldval, value)) return;

        myStorage[index] = value;
        myChange.Fire(ListEvent<T>.Update(index, oldval, value));        
      }
    }   

    public void Advise(Lifetime lifetime, Action<ListEvent<T>> handler)
    {
      for (int index=0; index < myStorage.Count; index++)
      {
        try
        {
          handler(ListEvent<T>.Add(index, myStorage[index]));
        }
        catch (Exception e)
        {
          Log.Root.Error(e);
        }
      }
      myChange.Advise(lifetime, handler);
    }

    public int IndexOf(T item)
    {
      return myStorage.IndexOf(item);
    }

    // ReSharper disable once AnnotationConflictInHierarchy
    public void Insert(int index, [NotNull] T item)
    {
      if (item == null) throw new ArgumentNullException(nameof(item));
      
      myStorage.Insert(index, item);
      myChange.Fire(ListEvent<T>.Add(index, item));
    }
  }
}