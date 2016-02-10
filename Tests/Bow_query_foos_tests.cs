using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Sqlcaster;
using Tests.Samples;
using System.Linq;

namespace Tests
{
    [TestClass]
    public class Bow_query_foos_tests
    {
        private readonly Bow bow;

        public Bow_query_foos_tests()
        {
            bow = new Bow();
        }

        [TestMethod]
        public async Task _warmup()
        {
            var foos = await bow.Query<Foo>()
                .ToListAsync();
        }

        [TestMethod]
        public async Task List_foos()
        {
            var foos = await bow.Query<Foo>()
                .ToListAsync();
        }

        [TestMethod]
        public async Task Filter_foos()
        {
            var foos = await bow.Query<Foo>()
                .Where("Int between @Start and @Finish")
                .ToListAsync(new { Start = 100, Finish = 200 });
        }

        [TestMethod]
        public async Task Filter_foos_with_array()
        {
            var foos = await bow.Query<Foo>()
                .Where("Int in @Values")
                .ToListAsync(new { Values = new[] { 100, 200, 300 } });
        }

        [TestMethod]
        public async Task Fill_foos_references()
        {
            var foos = await bow.Query<Foo>()
                .ToListAsync(page: 1, pageSize: 100);

            var foreigners = await bow.Query<Foreign>()
                .Where("Id in @Ids")
                .ToListAsync(new
                {
                    Ids = foos.Select(o => o.ForeignId).Where(o => o.HasValue).ToArray()
                });

            var fooRefs = await bow.Query<FooReference>()
                .Where("FooId in @Ids")
                .ToListAsync(new
                {
                    Ids = foos.Select(o => o.Id).ToArray()
                });

            var fooChildren = await bow.Query<FooChild>()
                .Where("FooId in @Ids")
                .ToListAsync(new
                {
                    Ids = foos.Select(o => o.Id).ToArray()
                });

            foos.FillWith(foreigners, o => o.Foreign, o => o.ForeignId, f => f.Id);
            foos.FillWith(fooRefs, o => o.Reference, o => o.Id, r => r.FooId);
            foos.FillWith(fooChildren, o => o.Children, o => o.Id, c => c.FooId);
        }

        [TestMethod]
        public async Task Fill_foos()
        {
            var foos = await bow.Query<Foo>()
                .ToListAsync(page: 1, pageSize: 100);

            await bow.Query<Foreign>()
                .Where("Id in @Ids")
                .FillAsync(foos, foo => foo.Foreign, foo => foo.ForeignId, foreign => foreign.Id);
        }

        [TestMethod]
        public async Task Select_foos()
        {
            var foos = await bow.Query<Foo>()
                .Select("Id, Int")
                .ToListAsync();
        }

        [TestMethod]
        public async Task Page_foos()
        {
            var foos = await bow.Query<Foo>()
                .ToListAsync(page: 2, pageSize: 500);
        }
    }
}
