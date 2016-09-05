![Logo](Art/Logo150x150.png "Logo")

# Genesis.WindowScan

[![Build status](https://ci.appveyor.com/api/projects/status/6xahxjp1ac5ly0g2?svg=true)](https://ci.appveyor.com/project/kentcb/genesis-windowscan)

## What?

> All Genesis.* projects are formalizations of small pieces of functionality I find myself copying from project to project. Some are small to the point of triviality, but are time-savers nonetheless. They have a particular focus on performance with respect to mobile development, but are certainly applicable outside this domain.
 
**Genesis.WindowScan** adds a `WindowScan` extension method to observables. This allows you to scan a time-boxed window if items. As items arrive on the source, your `add` function will be called. As items fall outside the window, your `remove` function will be called.

In effect, `WindowScan` allows you to answer questions such as:

* given an observable stream of customer orders, what is the total value of all orders placed in the last 6 months?
* given an observable stream of prices, what is the average price for the last 10 minutes?

**Genesis.WindowScan** is delivered as a PCL targeting a wide range of platforms, including:

* .NET 4.5
* Windows 8
* Windows Store
* Windows Phone 8
* Xamarin iOS
* Xamarin Android

## Why?

The `Scan` operator makes it possible to calculate intermediary results for each item received in a stream. The `Window` operator allows you to break a source observable into further observables by time. But neither makes it possible to scan a window of time.

## Where?

The easiest way to get **Genesis.WindowScan** is via [NuGet](http://www.nuget.org/packages/Genesis.WindowScan/):

```PowerShell
Install-Package Genesis.WindowScan
```

## How?

**Genesis.WindowScan** adds overloaded `WindowScan` extension methods to your observable sequences. It's defined in the `System.Reactive.Linq` namespace, so you'll generally have access to it if you're already using LINQ to Rx.

Here are some examples:

```C#
IObsevable<int> someObservable = ...;

var totalCountInLast10Seconds = someObservable
    .WindowScan(
        0,
        (acc, added) => acc + added,
        (acc, removed) => acc - removed,
        TimeSpan.FromSeconds(10),
        scheduler);

var averageInLastMinute = someObservable
    .WindowScan(
        0,
        // see http://math.stackexchange.com/questions/493607/find-new-average-if-removing-one-element-from-current-average for the Math
        (acc, count, add) => acc + ((add - acc) / count),
        (acc, count, remove) => (((acc * (count + 1)) - remove) / count),
        TimeSpan.FromMinutes(1),
        scheduler);
``` 

## Who?

**Genesis.WindowScan** is created and maintained by [Kent Boogaart](http://kent-boogaart.com). Issues and pull requests are welcome.