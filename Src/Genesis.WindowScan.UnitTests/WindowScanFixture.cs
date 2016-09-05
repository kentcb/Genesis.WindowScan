namespace Genesis.WindowScan.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Reactive;
    using System.Reactive.Subjects;
    using Microsoft.Reactive.Testing;
    using Xunit;

    public sealed class WindowScanFixture
    {
        [Fact]
        public void items_are_added_to_accumulate_as_they_appear_in_source()
        {
            var scheduler = new TestScheduler();
            var source = new Subject<int>();
            var sut = source
                .WindowScan(
                    0,
                    (acc, add) => acc + add,
                    (acc, remove) => acc - remove,
                    TimeSpan.FromSeconds(5),
                    scheduler);
            var values = new List<int>();
            sut.Subscribe(values.Add);

            scheduler.AdvanceTo("2016-01-01".ToDateTime().Value);
            source.OnNext(42);
            scheduler.AdvanceMinimal();
            Assert.Equal(1, values.Count);
            Assert.Equal(42, values[0]);

            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
            source.OnNext(13);
            scheduler.AdvanceMinimal();
            Assert.Equal(2, values.Count);
            Assert.Equal(55, values[1]);

            scheduler.AdvanceBy(TimeSpan.FromSeconds(2));
            source.OnNext(7);
            scheduler.AdvanceMinimal();
            Assert.Equal(3, values.Count);
            Assert.Equal(62, values[2]);
        }

        [Fact]
        public void items_are_removed_from_accumulate_as_they_fall_outside_the_period()
        {
            var scheduler = new TestScheduler();
            var source = new Subject<int>();
            var sut = source
                .WindowScan(
                    0,
                    (acc, add) => acc + add,
                    (acc, remove) => acc - remove,
                    TimeSpan.FromSeconds(3),
                    scheduler);
            var values = new List<int>();
            sut.Subscribe(values.Add);

            scheduler.AdvanceTo("2016-01-01".ToDateTime().Value);
            source.OnNext(1);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
            source.OnNext(2);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
            source.OnNext(3);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(0.5));
            Assert.Equal(3, values.Count);
            Assert.Equal(1 + 2 + 3, values[2]);

            // by advancing another half second, the first value we added (1) will exit the cache
            scheduler.AdvanceBy(TimeSpan.FromSeconds(0.5));
            Assert.Equal(4, values.Count);
            Assert.Equal(1 + 2 + 3 - 1, values[3]);

            scheduler.AdvanceBy(TimeSpan.FromSeconds(5));
            Assert.Equal(6, values.Count);
            Assert.Equal(1 + 2 + 3 - 1 - 2, values[4]);
            Assert.Equal(1 + 2 + 3 - 1 - 2 - 3, values[5]);
        }

        [Fact]
        public void callers_can_receive_count_of_items_within_window()
        {
            var scheduler = new TestScheduler();
            var source = new Subject<Unit>();
            var count = 0;
            var sut = source
                .WindowScan(
                    Unit.Default,
                    (acc, c, add) =>
                    {
                        count = c;
                        return Unit.Default;
                    },
                    (acc, c, remove) =>
                    {
                        count = c;
                        return Unit.Default;
                    },
                    TimeSpan.FromSeconds(3),
                    scheduler);
            sut.Subscribe();

            scheduler.AdvanceTo("2016-01-01".ToDateTime().Value);

            source.OnNext(Unit.Default);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
            Assert.Equal(1, count);

            source.OnNext(Unit.Default);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
            Assert.Equal(2, count);

            source.OnNext(Unit.Default);
            scheduler.AdvanceBy(TimeSpan.FromSeconds(0.5));
            Assert.Equal(3, count);

            scheduler.AdvanceBy(TimeSpan.FromSeconds(0.5));
            Assert.Equal(2, count);

            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
            Assert.Equal(1, count);

            scheduler.AdvanceBy(TimeSpan.FromSeconds(1));
            Assert.Equal(0, count);
        }

        [Fact]
        public void subscription_disposal_cleans_up_correctly()
        {
            var scheduler = new TestScheduler();
            var source = new Subject<int>();
            var sut = source
                .WindowScan(
                    0,
                    (acc, add) => acc + add,
                    (acc, remove) => acc - remove,
                    TimeSpan.FromSeconds(4),
                    scheduler);
            var values = new List<int>();
            var subscription = sut.Subscribe(values.Add);

            scheduler.AdvanceTo("2016-01-01".ToDateTime().Value);
            source.OnNext(1);
            scheduler.AdvanceMinimal();
            Assert.Equal(1, values.Count);

            subscription.Dispose();

            source.OnNext(2);
            scheduler.AdvanceMinimal();
            Assert.Equal(1, values.Count);
        }
    }
}