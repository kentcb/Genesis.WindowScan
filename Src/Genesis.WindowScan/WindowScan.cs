namespace Genesis.WindowScan
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reactive;
    using System.Reactive.Concurrency;
    using System.Reactive.Disposables;
    using System.Reactive.Linq;
    using System.Reactive.Subjects;

    /// <summary>
    /// Provides the <see cref="WindowScan"/> extension method.
    /// </summary>
    public static class ObservableExtensions
    {
        /// <summary>
        /// Scans a time-boxed window of items from the source.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method scans a time-boxed window of items, as dictated by the <paramref name="period"/> parameter. As items are received from the source,
        /// they are added to the accumulation via the <paramref name="add"/> function. As items fall outside the specified time-box, they are removed from
        /// the accumulation via the <paramref name="remove"/> function.
        /// </para>
        /// </remarks>
        /// <typeparam name="TSource">
        /// The type of the source items.
        /// </typeparam>
        /// <typeparam name="TAccumulate">
        /// The type of the accumulation.
        /// </typeparam>
        /// <param name="this">
        /// The source.
        /// </param>
        /// <param name="seed">
        /// The initial value assigned to the accumulation.
        /// </param>
        /// <param name="add">
        /// A function to add an item to the accumulation.
        /// </param>
        /// <param name="remove">
        /// A function to remove an item from the accumulation.
        /// </param>
        /// <param name="period">
        /// How long items should remain part of the accumulation.
        /// </param>
        /// <param name="scheduler">
        /// A scheduler to use.
        /// </param>
        /// <returns>
        /// An observable sequence whose items are of type <typeparamref name="TAccumulate"/>, where each value is the accumulation of all items currently
        /// within the given time-box.
        /// </returns>
        public static IObservable<TAccumulate> WindowScan<TSource, TAccumulate>(
            this IObservable<TSource> @this,
            TAccumulate seed,
            Func<TAccumulate, TSource, TAccumulate> add,
            Func<TAccumulate, TSource, TAccumulate> remove,
            TimeSpan period,
            IScheduler scheduler) =>
            @this.WindowScan(seed, (acc, c, x) => add(acc, x), (acc, c, x) => remove(acc, x), period, scheduler);

        /// <summary>
        /// Scans a time-boxed window of items from the source.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method scans a time-boxed window of items, as dictated by the <paramref name="period"/> parameter. As items are received from the source,
        /// they are added to the accumulation via the <paramref name="add"/> function. As items fall outside the specified time-box, they are removed from
        /// the accumulation via the <paramref name="remove"/> function.
        /// </para>
        /// <para>
        /// This overload takes <paramref name="add"/> and <paramref name="remove"/> functions that take an additional parameter telling the caller how many
        /// items currently fall within the window. The count provided is post add or remove. That is, the <paramref name="add"/> function will always receive
        /// a count >= 1, and the <paramref name="remove"/> function will receive a count of zero if there are no more items within the window.
        /// </para>
        /// </remarks>
        /// <typeparam name="TSource">
        /// The type of the source items.
        /// </typeparam>
        /// <typeparam name="TAccumulate">
        /// The type of the accumulation.
        /// </typeparam>
        /// <param name="this">
        /// The source.
        /// </param>
        /// <param name="seed">
        /// The initial value assigned to the accumulation.
        /// </param>
        /// <param name="add">
        /// A function to add an item to the accumulation. Also takes the total number of items now comprising the accumulation.
        /// </param>
        /// <param name="remove">
        /// A function to remove an item from the accumulation. Also takes the total number of items now comprising the accumulation.
        /// </param>
        /// <param name="period">
        /// How long items should remain part of the accumulation.
        /// </param>
        /// <param name="scheduler">
        /// A scheduler to use.
        /// </param>
        /// <returns>
        /// An observable sequence whose items are of type <typeparamref name="TAccumulate"/>, where each value is the accumulation of all items currently
        /// within the given time-box.
        /// </returns>
        public static IObservable<TAccumulate> WindowScan<TSource, TAccumulate>(
            this IObservable<TSource> @this,
            TAccumulate seed,
            Func<TAccumulate, int, TSource, TAccumulate> add,
            Func<TAccumulate, int, TSource, TAccumulate> remove,
            TimeSpan period,
            IScheduler scheduler) =>
            Observable
                .Create<TAccumulate>(
                    observer =>
                    {
                        var accumulation = seed;

                        // using a linked list because our data is sequential, never randomly accessed, and we need to efficiently remove items from the start
                        var cache = new LinkedList<Timestamped<TSource>>();

                        // ticks when we need to remove the oldest value
                        var removeOldestValues = new Subject<Unit>();

                        // keeps ahold of the scheduled action to remove oldest values
                        var removeOldestValuesDisposable = new SerialDisposable();

                        // adds items from the source to the cache
                        var addToCache = @this
                            .Timestamp(scheduler)
                            .ObserveOn(scheduler)
                            .Do(
                                timestampedValue =>
                                {
                                    AddToCache(cache, timestampedValue, (c, x) => accumulation = add(accumulation, c, x));
                                    EnsureRemovalIsScheduled(cache, removeOldestValues, removeOldestValuesDisposable, period, scheduler);
                                })
                            .Select(_ => Unit.Default);

                        // removes old values from the cache as necessary
                        var removeFromCache = removeOldestValues
                            .Do(_ => RemoveExpiredValuesFromCache(cache, (c, x) => accumulation = remove(accumulation, c, x), period, scheduler));

                        var maintainCache =
                            Observable
                                .Merge(
                                    addToCache,
                                    removeFromCache)
                            .Select(_ => accumulation)
                            .Subscribe(observer);
                        return new CompositeDisposable(
                            removeOldestValuesDisposable,
                            maintainCache);
                    });

        private static void AddToCache<TSource>(
            LinkedList<Timestamped<TSource>> cache,
            Timestamped<TSource> valueToAdd,
            Action<int, TSource> add)
        {
            cache.AddLast(valueToAdd);
            add(cache.Count, valueToAdd.Value);
        }

        private static void RemoveExpiredValuesFromCache<TSource>(
            LinkedList<Timestamped<TSource>> cache,
            Action<int, TSource> remove,
            TimeSpan period,
            IScheduler scheduler)
        {
            var now = scheduler.Now;
            var cacheStart = now.Subtract(period);

            // remove any stale entries (there could be multiple if they are very tightly packed)
            while (cache.Count > 0 && cache.First.Value.Timestamp <= cacheStart)
            {
                var valueToRemove = cache.First.Value.Value;
                cache.RemoveFirst();
                remove(cache.Count, valueToRemove);
            }
        }

        private static void EnsureRemovalIsScheduled<TSource>(
            LinkedList<Timestamped<TSource>> cache,
            IObserver<Unit> removeOldestValues,
            SerialDisposable removeOldestValuesDisposable,
            TimeSpan period,
            IScheduler scheduler)
        {
            if (removeOldestValuesDisposable.Disposable != null)
            {
                // removal is already scheduled
                return;
            }

            if (cache.Count == 0)
            {
                // nothing to remove
                return;
            }

            // schedule removal of oldest value
            var now = scheduler.Now;
            var cacheStart = now.Subtract(period);
            var oldestValue = cache.First.Value;
            var timeUntilExpiration = oldestValue.Timestamp - cacheStart;

            if (timeUntilExpiration < TimeSpan.Zero)
            {
                timeUntilExpiration = TimeSpan.Zero;
            }

            removeOldestValuesDisposable.Disposable = scheduler
                .Schedule(
                    timeUntilExpiration,
                    () =>
                    {
                        // tick a value to tell the pipeline to remove all values outside of the window
                        removeOldestValues.OnNext(Unit.Default);

                        // clear the disposable so we know that there is no scheduled action to remove stale items
                        removeOldestValuesDisposable.Disposable = null;

                        // re-schedule as necessary
                        EnsureRemovalIsScheduled(cache, removeOldestValues, removeOldestValuesDisposable, period, scheduler);
                    });
        }
    }
}